using AudiobookOrganizer.Api.Models.Local;

namespace AudiobookOrganizer.Api.Models.Api;

/// <summary>POST /api/jobs body: the book (group) to approve.</summary>
public record ApproveRequest(long? GroupId);

/// <summary>One file of a job: where it is and where it will go, as snapshotted at approval.</summary>
public record ProcessingJobFileDto(long FileId, string SourcePath, string DestinationPath, string Status, string? Error);

/// <summary>A processing job with its files in book order. GroupId is null once the group is deleted.</summary>
public record ProcessingJobDto(
    long Id, long? GroupId, string? Title, string Status, string CreatedAtUtc, string? StartedAtUtc,
    string? FinishedAtUtc, string? Error, IReadOnlyList<ProcessingJobFileDto> Files)
{
    public static ProcessingJobDto From(ProcessingJob job, IReadOnlyList<ProcessingJobFile> files) => new(
        job.Id, job.GroupId, job.Title, job.Status, job.CreatedAtUtc, job.StartedAtUtc, job.FinishedAtUtc, job.Error,
        files.Select(f => new ProcessingJobFileDto(f.AudioFileId, f.SourcePath, f.DestinationPath, f.Status, f.Error)).ToList());
}
