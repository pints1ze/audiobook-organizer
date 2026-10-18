namespace AudiobookOrganizer.Api.Data;

public class DatabaseInitializer(SqliteConnectionFactory connections)
{
    public void Initialize()
    {
        var directory = Path.GetDirectoryName(connections.DatabasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Opening the connection creates the file. Schema is added here in later slices.
        using var connection = connections.Open();
    }
}
