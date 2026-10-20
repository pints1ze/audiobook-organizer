using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;

namespace AudiobookOrganizer.Api.Services;

public enum ApproveOutcome { Approved, NotFound, Conflict, Invalid }

/// <summary>The approval's outcome: the job when Approved, the reason when Invalid.</summary>
public record ApproveResult(ApproveOutcome Outcome, ProcessingJobDto? Job = null, string? Message = null);

/// <summary>Approves books into the processing queue. Approval only records the job: nothing is copied or created.</summary>
public class ProcessingService(NamingPreviewService previews, ProcessingRepository jobs)
{
    // Keeps two approvals of the same book from both passing the active-job check.
    private readonly Lock _lock = new();

    /// <summary>
    /// Queues a job for the group when its preview is clean, snapshotting each file's source and destination in book
    /// order. Refused (Invalid) for no files, an error, missing values, a duplicate target or an existing destination,
    /// in that order; Conflict when the book already has a Queued or Running job.
    /// </summary>
    public ApproveResult Approve(long groupId)
    {
        lock (_lock)
        {
            if (previews.Preview(groupId) is not { } preview) return new ApproveResult(ApproveOutcome.NotFound);
            if (jobs.HasActiveJob(groupId)) return new ApproveResult(ApproveOutcome.Conflict);
            if (Refusal(preview.Files) is { } message) return new ApproveResult(ApproveOutcome.Invalid, Message: message);

            var job = new ProcessingJob
            {
                GroupId = groupId,
                Title = preview.Title,
                Status = ProcessingJob.Queued,
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
            };
            var files = preview.Files.Select(f => new ProcessingJobFile
            {
                AudioFileId = f.FileId,
                SourcePath = f.SourcePath,
                DestinationPath = f.DestinationPath!,
                Status = ProcessingJobFile.Pending,
            }).ToList();
            var id = jobs.CreateJob(job, files);
            var (saved, savedFiles) = jobs.GetJob(id)!.Value;
            return new ApproveResult(ApproveOutcome.Approved, ProcessingJobDto.From(saved, savedFiles));
        }
    }

    /// <summary>Why the book can't be approved, or null when every file has a clean destination.</summary>
    private static string? Refusal(IReadOnlyList<FilePreviewDto> files)
    {
        if (files.Count == 0) return "The book has no files.";
        if (files.FirstOrDefault(f => f.Error is not null) is { } error) return error.Error;
        var missing = files.SelectMany(f => f.Missing).Distinct().ToList();
        if (missing.Count > 0) return $"Fill in the missing values: {string.Join(", ", missing)}.";
        if (files.FirstOrDefault(f => f.Duplicate) is { } duplicate)
            return $"Two files would go to the same place: {duplicate.RelativePath}.";
        if (files.FirstOrDefault(f => f.Exists) is { } existing) return $"A file is already at {existing.RelativePath}.";
        return null;
    }

    public IReadOnlyList<ProcessingJobDto> GetJobs() =>
        jobs.GetJobs().Select(j => ProcessingJobDto.From(j.Job, j.Files)).ToList();

    public ProcessingJobDto? GetJob(long id) => jobs.GetJob(id) is { } found ? ProcessingJobDto.From(found.Job, found.Files) : null;
}
