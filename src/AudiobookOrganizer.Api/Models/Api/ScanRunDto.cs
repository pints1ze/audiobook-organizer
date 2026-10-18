using AudiobookOrganizer.Api.Models.Local;

namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>A scan run. Timestamps are ISO-8601 UTC; Status is Running, Completed or Failed.</summary>
public record ScanRunDto(
    long Id,
    string StartedAt,
    string? FinishedAt,
    string Status,
    int FilesFound,
    int FilesAdded,
    int FilesUpdated,
    int FilesUnchanged,
    int FilesMissing,
    int FilesFailed,
    string? Error)
{
    public static ScanRunDto From(ScanRun run) => new(run.Id, run.StartedAt, run.FinishedAt, run.Status,
        run.FilesFound, run.FilesAdded, run.FilesUpdated, run.FilesUnchanged, run.FilesMissing, run.FilesFailed, run.Error);
}
