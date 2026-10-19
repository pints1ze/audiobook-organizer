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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
