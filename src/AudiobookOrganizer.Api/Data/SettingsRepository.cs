using Dapper;

namespace AudiobookOrganizer.Api.Data;

public class SettingsRepository(SqliteConnectionFactory connections)
{
    public const string IncomingPath = "IncomingPath";
    public const string LibraryPath = "LibraryPath";
    public const string KeepSourceFiles = "KeepSourceFiles";

    public string? Get(string key)
    {
        using var connection = connections.Open();
        return connection.QuerySingleOrDefault<string>(
            "SELECT Value FROM ApplicationSetting WHERE Key = @key", new { key });
    }

    public void SetMany(IReadOnlyDictionary<string, string> values)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var (key, value) in values)
        {
            connection.Execute(
                """
                INSERT INTO ApplicationSetting (Key, Value) VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                """,
                new { key, value }, transaction);
        }
        transaction.Commit();
    }
}
