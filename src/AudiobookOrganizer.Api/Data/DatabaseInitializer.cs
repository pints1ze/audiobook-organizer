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
