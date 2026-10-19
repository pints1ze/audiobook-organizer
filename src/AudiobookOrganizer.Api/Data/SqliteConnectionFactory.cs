using Microsoft.Data.Sqlite;

namespace AudiobookOrganizer.Api.Data;

public class SqliteConnectionFactory(string databasePath)
{
    private readonly string _connectionString =
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public string DatabasePath { get; } = databasePath;

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        // SQLite enforces foreign keys (and ON DELETE CASCADE) only when each connection asks for it.
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON";
            pragma.ExecuteNonQuery();
        }
        return connection;
    }
}
