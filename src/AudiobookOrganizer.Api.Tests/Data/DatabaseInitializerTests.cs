using AudiobookOrganizer.Api.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AudiobookOrganizer.Api.Tests.Data;

public class DatabaseInitializerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"audiobook-organizer-init-{Guid.NewGuid():N}");

    [Fact]
    public void Initialize_creates_missing_nested_directory_and_database_file()
    {
        var path = Path.Combine(_root, "nested", "deeper", "audiobooks.db");
        Assert.False(Directory.Exists(_root));

        new DatabaseInitializer(new SqliteConnectionFactory(path)).Initialize();

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Initialize_adds_UserCorrected_to_an_existing_FileGroup_table_without_losing_rows()
    {
        Directory.CreateDirectory(_root);
        var connections = new SqliteConnectionFactory(Path.Combine(_root, "old.db"));
        using (var connection = connections.Open())
        {
            // The FileGroup schema from before slice 13, with no UserCorrected column.
            connection.Execute(
                """
                CREATE TABLE ScanRun (Id INTEGER PRIMARY KEY, StartedAt TEXT NOT NULL, FinishedAt TEXT, Status TEXT NOT NULL,
                    FilesFound INTEGER NOT NULL DEFAULT 0, FilesAdded INTEGER NOT NULL DEFAULT 0,
                    FilesUpdated INTEGER NOT NULL DEFAULT 0, FilesUnchanged INTEGER NOT NULL DEFAULT 0,
                    FilesMissing INTEGER NOT NULL DEFAULT 0, FilesFailed INTEGER NOT NULL DEFAULT 0, Error TEXT);
                CREATE TABLE FileGroup (Id INTEGER PRIMARY KEY, Title TEXT, Author TEXT, Confidence TEXT NOT NULL,
                    EvidenceJson TEXT NOT NULL, FileCount INTEGER NOT NULL, TotalDurationMs INTEGER NOT NULL,
                    ScanRunId INTEGER NOT NULL REFERENCES ScanRun (Id));
                INSERT INTO ScanRun (Id, StartedAt, Status) VALUES (1, '2026-10-18T00:00:00Z', 'Completed');
                INSERT INTO FileGroup (Id, Title, Author, Confidence, EvidenceJson, FileCount, TotalDurationMs, ScanRunId)
                VALUES (7, 'Old Book', 'Old Author', 'High', '[]', 2, 1000, 1);
                """);
        }

        var initializer = new DatabaseInitializer(connections);
        initializer.Initialize();
        initializer.Initialize();

        using var check = connections.Open();
        var row = check.QuerySingle<(long Id, string Title, string Author, long UserCorrected)>(
            "SELECT Id, Title, Author, UserCorrected FROM FileGroup");
        Assert.Equal((7L, "Old Book", "Old Author", 0L), row);
    }

    [Fact]
    public void Initialize_adds_UserState_to_an_existing_AudioFile_table_without_losing_rows()
    {
        Directory.CreateDirectory(_root);
        var connections = new SqliteConnectionFactory(Path.Combine(_root, "old.db"));
        using (var connection = connections.Open())
        {
            // The AudioFile schema from before slice 20, with no UserState column.
            connection.Execute(
                """
                CREATE TABLE ScanRun (Id INTEGER PRIMARY KEY, StartedAt TEXT NOT NULL, FinishedAt TEXT, Status TEXT NOT NULL,
                    FilesFound INTEGER NOT NULL DEFAULT 0, FilesAdded INTEGER NOT NULL DEFAULT 0,
                    FilesUpdated INTEGER NOT NULL DEFAULT 0, FilesUnchanged INTEGER NOT NULL DEFAULT 0,
                    FilesMissing INTEGER NOT NULL DEFAULT 0, FilesFailed INTEGER NOT NULL DEFAULT 0, Error TEXT);
                CREATE TABLE AudioFile (Id INTEGER PRIMARY KEY, SourcePath TEXT NOT NULL UNIQUE, FileName TEXT NOT NULL,
                    Extension TEXT NOT NULL, SizeBytes INTEGER NOT NULL, ModifiedAtUtc TEXT NOT NULL, DurationMs INTEGER,
                    Title TEXT, Album TEXT, Artist TEXT, AlbumArtist TEXT, Composer TEXT, Genre TEXT, Year INTEGER,
                    Track INTEGER, Disc INTEGER, Comment TEXT, HasCover INTEGER NOT NULL DEFAULT 0, ChapterCount INTEGER,
                    ScanError TEXT, FirstSeenScanId INTEGER NOT NULL REFERENCES ScanRun (Id),
                    LastSeenScanId INTEGER NOT NULL REFERENCES ScanRun (Id));
                INSERT INTO ScanRun (Id, StartedAt, Status) VALUES (1, '2026-10-18T00:00:00Z', 'Completed');
                INSERT INTO AudioFile (Id, SourcePath, FileName, Extension, SizeBytes, ModifiedAtUtc, FirstSeenScanId, LastSeenScanId)
                VALUES (5, '/incoming/a.mp3', 'a.mp3', '.mp3', 100, '2026-10-18T00:00:00Z', 1, 1);
                """);
        }

        var initializer = new DatabaseInitializer(connections);
        initializer.Initialize();
        initializer.Initialize();

        using var check = connections.Open();
        var row = check.QuerySingle<(long Id, string SourcePath, string? UserState)>(
            "SELECT Id, SourcePath, UserState FROM AudioFile");
        Assert.Equal((5L, "/incoming/a.mp3", (string?)null), row);
    }

    /// <summary>A database whose FileGroup was created before slice 24, without AUTOINCREMENT, holding groups 1 and 5.</summary>
    private SqliteConnectionFactory OldDatabaseWithoutAutoincrement()
    {
        Directory.CreateDirectory(_root);
        var connections = new SqliteConnectionFactory(Path.Combine(_root, "old.db"));
        using var connection = connections.Open();
        connection.Execute(
            """
            CREATE TABLE ScanRun (Id INTEGER PRIMARY KEY, StartedAt TEXT NOT NULL, FinishedAt TEXT, Status TEXT NOT NULL,
                FilesFound INTEGER NOT NULL DEFAULT 0, FilesAdded INTEGER NOT NULL DEFAULT 0,
                FilesUpdated INTEGER NOT NULL DEFAULT 0, FilesUnchanged INTEGER NOT NULL DEFAULT 0,
                FilesMissing INTEGER NOT NULL DEFAULT 0, FilesFailed INTEGER NOT NULL DEFAULT 0, Error TEXT);
            CREATE TABLE AudioFile (Id INTEGER PRIMARY KEY, SourcePath TEXT NOT NULL UNIQUE, FileName TEXT NOT NULL,
                Extension TEXT NOT NULL, SizeBytes INTEGER NOT NULL, ModifiedAtUtc TEXT NOT NULL, DurationMs INTEGER,
                Title TEXT, Album TEXT, Artist TEXT, AlbumArtist TEXT, Composer TEXT, Genre TEXT, Year INTEGER,
                Track INTEGER, Disc INTEGER, Comment TEXT, HasCover INTEGER NOT NULL DEFAULT 0, ChapterCount INTEGER,
                ScanError TEXT, FirstSeenScanId INTEGER NOT NULL REFERENCES ScanRun (Id),
                LastSeenScanId INTEGER NOT NULL REFERENCES ScanRun (Id), UserState TEXT);
            CREATE TABLE FileGroup (Id INTEGER PRIMARY KEY, Title TEXT, Author TEXT, Confidence TEXT NOT NULL,
                EvidenceJson TEXT NOT NULL, FileCount INTEGER NOT NULL, TotalDurationMs INTEGER NOT NULL,
                ScanRunId INTEGER NOT NULL REFERENCES ScanRun (Id), UserCorrected INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE FileGroupFile (GroupId INTEGER NOT NULL REFERENCES FileGroup (Id) ON DELETE CASCADE,
                AudioFileId INTEGER NOT NULL UNIQUE REFERENCES AudioFile (Id), Position INTEGER NOT NULL,
                Disc INTEGER, Track INTEGER);
            INSERT INTO ScanRun (Id, StartedAt, Status) VALUES (1, '2026-10-18T00:00:00Z', 'Completed');
            INSERT INTO AudioFile (Id, SourcePath, FileName, Extension, SizeBytes, ModifiedAtUtc, FirstSeenScanId, LastSeenScanId)
            VALUES (10, '/incoming/a.mp3', 'a.mp3', '.mp3', 100, '2026-10-18T00:00:00Z', 1, 1),
                   (11, '/incoming/b.m4b', 'b.m4b', '.m4b', 200, '2026-10-18T00:00:00Z', 1, 1);
            INSERT INTO FileGroup (Id, Title, Author, Confidence, EvidenceJson, FileCount, TotalDurationMs, ScanRunId, UserCorrected)
            VALUES (1, 'Book One', 'Author One', 'High', '["a"]', 1, 1000, 1, 1),
                   (5, 'Book Five', NULL, 'Low', '["b"]', 1, 2000, 1, 0);
            INSERT INTO FileGroupFile (GroupId, AudioFileId, Position, Disc, Track) VALUES (1, 10, 1, NULL, 3), (5, 11, 1, 2, NULL);
            """);
        return connections;
    }

    private static string FileGroupSql(SqliteConnectionFactory connections)
    {
        using var connection = connections.Open();
        return connection.ExecuteScalar<string>("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'FileGroup'")!;
    }

    [Fact]
    public void Initialize_rebuilds_FileGroup_with_AUTOINCREMENT_keeping_every_row_and_id()
    {
        var connections = OldDatabaseWithoutAutoincrement();
        Assert.DoesNotContain("AUTOINCREMENT", FileGroupSql(connections), StringComparison.OrdinalIgnoreCase);

        var initializer = new DatabaseInitializer(connections);
        initializer.Initialize();

        var rebuiltSql = FileGroupSql(connections);
        Assert.Contains("AUTOINCREMENT", rebuiltSql, StringComparison.OrdinalIgnoreCase);
        using (var check = connections.Open())
        {
            var groups = check.Query<(long Id, string Title, string? Author, string Confidence, string EvidenceJson,
                    long FileCount, long TotalDurationMs, long ScanRunId, long UserCorrected)>(
                    "SELECT Id, Title, Author, Confidence, EvidenceJson, FileCount, TotalDurationMs, ScanRunId, UserCorrected FROM FileGroup ORDER BY Id")
                .ToList();
            Assert.Equal(
                [(1L, "Book One", "Author One", "High", """["a"]""", 1L, 1000L, 1L, 1L),
                 (5L, "Book Five", null, "Low", """["b"]""", 1L, 2000L, 1L, 0L)],
                groups);
            var files = check.Query<(long GroupId, long AudioFileId, long Position, long? Disc, long? Track)>(
                "SELECT GroupId, AudioFileId, Position, Disc, Track FROM FileGroupFile ORDER BY GroupId").ToList();
            Assert.Equal([(1L, 10L, 1L, (long?)null, (long?)3), (5L, 11L, 1L, (long?)2, (long?)null)], files);
            Assert.Empty(check.Query("PRAGMA foreign_key_check"));
        }

        initializer.Initialize();

        Assert.Equal(rebuiltSql, FileGroupSql(connections));
        using var again = connections.Open();
        Assert.Equal([1L, 5L], again.Query<long>("SELECT Id FROM FileGroup ORDER BY Id"));
        Assert.Equal(2, again.ExecuteScalar<long>("SELECT COUNT(*) FROM FileGroupFile"));
        Assert.Empty(again.Query("PRAGMA foreign_key_check"));
    }

    [Fact]
    public void After_the_rebuild_a_deleted_highest_id_is_never_reused()
    {
        var connections = OldDatabaseWithoutAutoincrement();
        new DatabaseInitializer(connections).Initialize();

        using var connection = connections.Open();
        connection.Execute("DELETE FROM FileGroup WHERE Id = 5");
        var id = connection.ExecuteScalar<long>(
            """
            INSERT INTO FileGroup (Title, Confidence, EvidenceJson, FileCount, TotalDurationMs, ScanRunId)
            VALUES ('New', 'High', '[]', 0, 0, 1) RETURNING Id
            """);

        Assert.True(id > 5, $"Expected an id above 5, got {id}");
    }

    [Fact]
    public void New_databases_create_FileGroup_with_AUTOINCREMENT()
    {
        var connections = new SqliteConnectionFactory(Path.Combine(_root, "new.db"));

        new DatabaseInitializer(connections).Initialize();

        Assert.Contains("AUTOINCREMENT", FileGroupSql(connections), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
