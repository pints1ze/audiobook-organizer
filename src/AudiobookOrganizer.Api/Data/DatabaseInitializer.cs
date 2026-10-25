using Dapper;
using Microsoft.Data.Sqlite;

namespace AudiobookOrganizer.Api.Data;

public class DatabaseInitializer(SqliteConnectionFactory connections)
{
    public void Initialize()
    {
        var directory = Path.GetDirectoryName(connections.DatabasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Opening the connection creates the file. Each table is created idempotently; there's no migration framework.
        using var connection = connections.Open();
        connection.Execute("CREATE TABLE IF NOT EXISTS ApplicationSetting (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)");
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ScanRun (
                Id INTEGER PRIMARY KEY,
                StartedAt TEXT NOT NULL,
                FinishedAt TEXT,
                Status TEXT NOT NULL,
                FilesFound INTEGER NOT NULL DEFAULT 0,
                FilesAdded INTEGER NOT NULL DEFAULT 0,
                FilesUpdated INTEGER NOT NULL DEFAULT 0,
                FilesUnchanged INTEGER NOT NULL DEFAULT 0,
                FilesMissing INTEGER NOT NULL DEFAULT 0,
                FilesFailed INTEGER NOT NULL DEFAULT 0,
                Error TEXT)
            """);
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS AudioFile (
                Id INTEGER PRIMARY KEY,
                SourcePath TEXT NOT NULL UNIQUE,
                FileName TEXT NOT NULL,
                Extension TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                ModifiedAtUtc TEXT NOT NULL,
                DurationMs INTEGER,
                Title TEXT,
                Album TEXT,
                Artist TEXT,
                AlbumArtist TEXT,
                Composer TEXT,
                Genre TEXT,
                Year INTEGER,
                Track INTEGER,
                Disc INTEGER,
                Comment TEXT,
                HasCover INTEGER NOT NULL DEFAULT 0,
                ChapterCount INTEGER,
                ScanError TEXT,
                FirstSeenScanId INTEGER NOT NULL REFERENCES ScanRun (Id),
                LastSeenScanId INTEGER NOT NULL REFERENCES ScanRun (Id),
                UserState TEXT)
            """);
        // Databases created before slice 20 have AudioFile without UserState ('Excluded', 'Unresolved' or null).
        if (!connection.Query<string>("SELECT name FROM pragma_table_info('AudioFile')").Contains("UserState"))
            connection.Execute("ALTER TABLE AudioFile ADD COLUMN UserState TEXT");
        connection.Execute($"CREATE TABLE IF NOT EXISTS FileGroup {FileGroupColumns}");
        // Databases created before slice 13 have FileGroup without UserCorrected.
        if (!connection.Query<string>("SELECT name FROM pragma_table_info('FileGroup')").Contains("UserCorrected"))
            connection.Execute("ALTER TABLE FileGroup ADD COLUMN UserCorrected INTEGER NOT NULL DEFAULT 0");
        // Databases created before slice 24 have FileGroup without AUTOINCREMENT, so a deleted id could be reused.
        if (!connection.ExecuteScalar<string>(
                "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'FileGroup'")!
            .Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase))
            RebuildFileGroupWithAutoincrement(connection);
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS FileGroupFile (
                GroupId INTEGER NOT NULL REFERENCES FileGroup (Id) ON DELETE CASCADE,
                AudioFileId INTEGER NOT NULL UNIQUE REFERENCES AudioFile (Id),
                Position INTEGER NOT NULL,
                Disc INTEGER,
                Track INTEGER)
            """);
        connection.Execute("CREATE INDEX IF NOT EXISTS IX_FileGroupFile_GroupId ON FileGroupFile (GroupId)");
        // One Audiobook per group and one current match per Audiobook; both go when their group is deleted.
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS Audiobook (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GroupId INTEGER NOT NULL UNIQUE REFERENCES FileGroup (Id) ON DELETE CASCADE,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL)
            """);
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS MetadataMatch (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AudiobookId INTEGER NOT NULL UNIQUE REFERENCES Audiobook (Id) ON DELETE CASCADE,
                Provider TEXT NOT NULL,
                ProviderId TEXT,
                CandidateJson TEXT NOT NULL,
                PickedAtUtc TEXT NOT NULL)
            """);
        // A book's metadata values, one row per field. ValueJson is "text", ["a","b"] or null, so text and list
        // fields share the table. Origin is file, provider or user; Provider is set only for provider values.
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS AudiobookField (
                AudiobookId INTEGER NOT NULL REFERENCES Audiobook (Id) ON DELETE CASCADE,
                Field TEXT NOT NULL,
                ValueJson TEXT NOT NULL,
                Origin TEXT NOT NULL,
                Provider TEXT,
                PRIMARY KEY (AudiobookId, Field))
            """);
        // The book's chosen cover, one per Audiobook. Source is "embedded" (FileId is the file it came from); the image
        // is copied in, so the choice survives the source file being moved.
        // shortcut: covers stored as BLOBs — revisit if the database grows past a few hundred MB
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS AudiobookCover (
                AudiobookId INTEGER PRIMARY KEY REFERENCES Audiobook (Id) ON DELETE CASCADE,
                Source TEXT NOT NULL,
                FileId INTEGER,
                MimeType TEXT NOT NULL,
                Image BLOB NOT NULL,
                ChosenAtUtc TEXT NOT NULL)
            """);
        // Canonical people (authors and narrators) and their name variants. The keys are CanonicalNameService.NameKey
        // values; the service keeps a key from appearing as both one person's name and another's alias.
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS Person (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                NameKey TEXT NOT NULL UNIQUE)
            """);
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS PersonAlias (
                PersonId INTEGER NOT NULL REFERENCES Person (Id) ON DELETE CASCADE,
                Alias TEXT NOT NULL,
                AliasKey TEXT NOT NULL UNIQUE,
                PRIMARY KEY (PersonId, AliasKey))
            """);
        // Canonical genres and their variants, the same shape as the person tables but keyed by
        // CanonicalNameService.GenreKey. People and genres are separate namespaces.
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS Genre (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                NameKey TEXT NOT NULL UNIQUE)
            """);
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS GenreAlias (
                GenreId INTEGER NOT NULL REFERENCES Genre (Id) ON DELETE CASCADE,
                Alias TEXT NOT NULL,
                AliasKey TEXT NOT NULL UNIQUE,
                PRIMARY KEY (GenreId, AliasKey))
            """);
        // Approved books and their files with the destinations snapshotted at approval. A job keeps its history when
        // its group is deleted (GroupId becomes null); its files go with the job.
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ProcessingJob (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GroupId INTEGER REFERENCES FileGroup (Id) ON DELETE SET NULL,
                Title TEXT,
                Status TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                StartedAtUtc TEXT,
                FinishedAtUtc TEXT,
                Error TEXT)
            """);
        connection.Execute("CREATE INDEX IF NOT EXISTS IX_ProcessingJob_GroupId ON ProcessingJob (GroupId)");
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ProcessingJobFile (
                JobId INTEGER NOT NULL REFERENCES ProcessingJob (Id) ON DELETE CASCADE,
                AudioFileId INTEGER NOT NULL,
                Position INTEGER NOT NULL,
                SourcePath TEXT NOT NULL,
                DestinationPath TEXT NOT NULL,
                Status TEXT NOT NULL,
                Error TEXT,
                PRIMARY KEY (JobId, Position))
            """);
        // A book's last catalog search: when it ran, the provider errors (a JSON string array) and its top candidates
        // by score, rank 0 first. Both go with the group; a new search replaces them.
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS CandidateSearch (
                GroupId INTEGER PRIMARY KEY REFERENCES FileGroup (Id) ON DELETE CASCADE,
                SearchedAtUtc TEXT NOT NULL,
                ErrorsJson TEXT NOT NULL)
            """);
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS MetadataCandidate (
                GroupId INTEGER NOT NULL REFERENCES CandidateSearch (GroupId) ON DELETE CASCADE,
                Rank INTEGER NOT NULL,
                Provider TEXT NOT NULL,
                CandidateJson TEXT NOT NULL,
                Score INTEGER NOT NULL,
                PRIMARY KEY (GroupId, Rank))
            """);
    }

    // AUTOINCREMENT keeps SQLite from reusing the id of a deleted group, so a page holding an old id gets a 404
    // instead of editing a different book.
    private const string FileGroupColumns =
        """
        (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Title TEXT,
            Author TEXT,
            Confidence TEXT NOT NULL,
            EvidenceJson TEXT NOT NULL,
            FileCount INTEGER NOT NULL,
            TotalDurationMs INTEGER NOT NULL,
            ScanRunId INTEGER NOT NULL REFERENCES ScanRun (Id),
            UserCorrected INTEGER NOT NULL DEFAULT 0)
        """;

    /// <summary>
    /// SQLite can't add AUTOINCREMENT with ALTER TABLE, so this follows its documented table-change procedure:
    /// create the new table, copy every row (ids included), drop the old one and rename the new one. It runs in one
    /// transaction and rolls back, leaving the old table in place, if anything fails or a foreign key breaks.
    /// FileGroupFile refers to FileGroup by name, so after the rename it points to the new table.
    /// </summary>
    private static void RebuildFileGroupWithAutoincrement(SqliteConnection connection)
    {
        const string columns = "Id, Title, Author, Confidence, EvidenceJson, FileCount, TotalDurationMs, ScanRunId, UserCorrected";
        // foreign_keys can't change inside a transaction, and dropping FileGroup with it on would cascade-delete FileGroupFile.
        connection.Execute("PRAGMA foreign_keys = OFF");
        try
        {
            using var transaction = connection.BeginTransaction();
            connection.Execute($"CREATE TABLE FileGroup_new {FileGroupColumns}", transaction: transaction);
            connection.Execute($"INSERT INTO FileGroup_new ({columns}) SELECT {columns} FROM FileGroup", transaction: transaction);
            connection.Execute("DROP TABLE FileGroup", transaction: transaction);
            connection.Execute("ALTER TABLE FileGroup_new RENAME TO FileGroup", transaction: transaction);
            if (connection.Query("PRAGMA foreign_key_check", transaction: transaction).Any())
                throw new InvalidOperationException("Rebuilding FileGroup broke a foreign key; the rebuild was rolled back.");
            transaction.Commit();
        }
        finally
        {
            connection.Execute("PRAGMA foreign_keys = ON");
        }
    }
}
