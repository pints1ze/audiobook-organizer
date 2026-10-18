namespace AudiobookOrganizer.Api.Configuration;

public class DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>SQLite file path. Relative paths resolve against the content root.</summary>
    public string Path { get; set; } = "data/audiobooks.db";
}
