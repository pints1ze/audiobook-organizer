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
    }
}
