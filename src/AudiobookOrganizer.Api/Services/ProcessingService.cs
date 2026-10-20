using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;
using AudiobookOrganizer.Api.Models.Local;

namespace AudiobookOrganizer.Api.Services;

public enum ApproveOutcome { Approved, NotFound, Conflict, Invalid }

/// <summary>The approval's outcome: the job when Approved, the reason when Invalid.</summary>
public record ApproveResult(ApproveOutcome Outcome, ProcessingJobDto? Job = null, string? Message = null);

/// <summary>
/// Approves books into the processing queue and runs queued jobs. Approval only records the job; <see cref="Run"/>
/// copies its files. Sources are only ever read.
/// </summary>
public class ProcessingService(NamingPreviewService previews, ProcessingRepository jobs, ILogger<ProcessingService> logger)
{
    public const string Interrupted = "Interrupted by an application restart.";

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

    /// <summary>
    /// Runs a Queued job: copies each file, in position order, to its snapshotted destination through a
    /// <c>.partial</c> file in the destination folder, never overwriting anything. The first problem fails that file
    /// and the job, and removes the copies this job made. Does nothing when the job isn't Queued.
    /// </summary>
    public void Run(long jobId, CancellationToken cancellationToken)
    {
        if (jobs.GetJob(jobId) is not { } found || found.Job.Status != ProcessingJob.Queued) return;
        var (job, files) = found;

        job.Status = ProcessingJob.Running;
        job.StartedAtUtc = DateTime.UtcNow.ToString("O");
        jobs.UpdateJob(job);

        string? partial = null;
        string? error = null;
        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                error = Interrupted;
                break;
            }
            try
            {
                error = Copy(file, ref partial);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            if (error is not null)
            {
                file.Status = ProcessingJobFile.Failed;
                file.Error = error;
                jobs.UpdateFile(file);
                break;
            }
            file.Status = ProcessingJobFile.Copied;
            jobs.UpdateFile(file);
        }

        if (error is not null) RemoveCopies(job, files, partial);
        job.Status = error is null ? ProcessingJob.Succeeded : ProcessingJob.Failed;
        job.Error = error;
        job.FinishedAtUtc = DateTime.UtcNow.ToString("O");
        jobs.UpdateJob(job);
    }

    /// <summary>
    /// Copies one file into place, or returns why it can't. <paramref name="partial"/> is set to the temporary file
    /// once this job has created it, and cleared once it's moved into place.
    /// </summary>
    private static string? Copy(ProcessingJobFile file, ref string? partial)
    {
        var destination = file.DestinationPath;
        if (!File.Exists(file.SourcePath)) return $"The source file is gone: {file.SourcePath}.";
        if (File.Exists(destination) || Directory.Exists(destination)) return $"A file is already at {destination}.";
        var temp = destination + ".partial";
        if (File.Exists(temp) || Directory.Exists(temp)) return $"A file is already at {temp}.";

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using (var source = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        // CreateNew throws if the temp file appeared since the check, so someone else's file is never replaced; once
        // it succeeds the file is this job's, and is removed if the copy fails part-way.
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            partial = temp;
            source.CopyTo(output);
        }
        if (new FileInfo(temp).Length != new FileInfo(file.SourcePath).Length)
            return $"The copy of {file.SourcePath} isn't the same size as the source.";
        // Same folder, so this is a rename; overwrite: false refuses a destination that appeared meanwhile.
        File.Move(temp, destination, overwrite: false);
        partial = null;
        return null;
    }

    /// <summary>
    /// Deletes the destinations this job copied (resetting those files to Pending) and its own temporary file.
    /// shortcut: folders the job created stay, possibly empty — revisit in phase 9 cleanup.
    /// </summary>
    private void RemoveCopies(ProcessingJob job, IReadOnlyList<ProcessingJobFile> files, string? partial)
    {
        foreach (var path in files.Where(f => f.Status == ProcessingJobFile.Copied).Select(f => f.DestinationPath).Append(partial))
        {
            if (path is null) continue;
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Couldn't remove {Path} after job {JobId} failed", path, job.Id);
            }
        }
        foreach (var file in files.Where(f => f.Status == ProcessingJobFile.Copied))
        {
            file.Status = ProcessingJobFile.Pending;
            jobs.UpdateFile(file);
        }
    }

    /// <summary>Marks a job Failed with the message, whatever state it was left in (the worker's last resort).</summary>
    public void Fail(long jobId, string message)
    {
        if (jobs.GetJob(jobId) is not { } found) return;
        var job = found.Job;
        job.Status = ProcessingJob.Failed;
        job.Error = message;
        job.FinishedAtUtc = DateTime.UtcNow.ToString("O");
        jobs.UpdateJob(job);
    }

    public IReadOnlyList<ProcessingJobDto> GetJobs() =>
        jobs.GetJobs().Select(j => ProcessingJobDto.From(j.Job, j.Files)).ToList();

    public ProcessingJobDto? GetJob(long id) => jobs.GetJob(id) is { } found ? ProcessingJobDto.From(found.Job, found.Files) : null;
}
