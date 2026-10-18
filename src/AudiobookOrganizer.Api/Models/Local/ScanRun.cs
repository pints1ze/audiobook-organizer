namespace AudiobookOrganizer.Api.Models.Local;

/// <summary>One scan of the incoming directory. Timestamps are ISO-8601 UTC text.</summary>
public class ScanRun
{
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";

    public long Id { get; set; }
    public string StartedAt { get; set; } = "";
    public string? FinishedAt { get; set; }
    public string Status { get; set; } = Running;
    public int FilesFound { get; set; }
    public int FilesAdded { get; set; }
    public int FilesUpdated { get; set; }
    public int FilesUnchanged { get; set; }
    public int FilesMissing { get; set; }
    public int FilesFailed { get; set; }
    public string? Error { get; set; }
}
