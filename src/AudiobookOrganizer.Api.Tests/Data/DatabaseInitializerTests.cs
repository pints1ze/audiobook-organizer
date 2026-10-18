using AudiobookOrganizer.Api.Data;
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

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
