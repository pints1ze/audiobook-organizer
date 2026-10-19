using Dapper;

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
                LastSeenScanId INTEGER NOT NULL REFERENCES ScanRun (Id))
            """);
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS FileGroup (
                Id INTEGER PRIMARY KEY,
                Title TEXT,
                Author TEXT,
                Confidence TEXT NOT NULL,
                EvidenceJson TEXT NOT NULL,
                FileCount INTEGER NOT NULL,
                TotalDurationMs INTEGER NOT NULL,
                ScanRunId INTEGER NOT NULL REFERENCES ScanRun (Id),
                UserCorrected INTEGER NOT NULL DEFAULT 0)
            """);
        // Databases created before slice 13 have FileGroup without UserCorrected.
        if (!connection.Query<string>("SELECT name FROM pragma_table_info('FileGroup')").Contains("UserCorrected"))
            connection.Execute("ALTER TABLE FileGroup ADD COLUMN UserCorrected INTEGER NOT NULL DEFAULT 0");
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
}
