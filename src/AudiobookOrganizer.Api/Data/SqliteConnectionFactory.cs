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
        return connection;
    }
}
