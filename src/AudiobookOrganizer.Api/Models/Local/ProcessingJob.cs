namespace AudiobookOrganizer.Api.Models.Local;

/// <summary>
/// One approved book waiting for or going through processing. GroupId is null once the group is deleted, so the job
/// outlives it. Timestamps are ISO-8601 UTC text.
/// </summary>
public class ProcessingJob
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";

    public long Id { get; set; }
    public long? GroupId { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = Queued;
    public string CreatedAtUtc { get; set; } = "";
    public string? StartedAtUtc { get; set; }
    public string? FinishedAtUtc { get; set; }
    public string? Error { get; set; }
}

/// <summary>One file of a job: its source and the destination snapshotted at approval, in book order (Position from 1).</summary>
public class ProcessingJobFile
{
    public const string Pending = "Pending";

    public long JobId { get; set; }
    public long AudioFileId { get; set; }
    public int Position { get; set; }
    public string SourcePath { get; set; } = "";
    public string DestinationPath { get; set; } = "";
    public string Status { get; set; } = Pending;
    public string? Error { get; set; }
}
