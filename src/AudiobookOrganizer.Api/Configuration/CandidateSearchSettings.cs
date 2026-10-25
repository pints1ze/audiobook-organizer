namespace AudiobookOrganizer.Api.Configuration;

public class CandidateSearchSettings
{
    public const string SectionName = "CandidateSearch";

    /// <summary>How long the background search waits between two books' searches.</summary>
    public int DelayMs { get; set; } = 2000;
}
