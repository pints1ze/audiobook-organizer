using AudiobookOrganizer.Api.Models.Local;
using Dapper;

namespace AudiobookOrganizer.Api.Data;

public class ProcessingRepository(SqliteConnectionFactory connections)
{
    private const string JobColumns = "Id, GroupId, Title, Status, CreatedAtUtc, StartedAtUtc, FinishedAtUtc, Error";
    private const string FileColumns = "JobId, AudioFileId, Position, SourcePath, DestinationPath, Status, Error";

    /// <summary>True when the group has a Queued or Running job.</summary>
    public bool HasActiveJob(long groupId)
    {
        using var connection = connections.Open();
        return connection.ExecuteScalar<bool>(
            "SELECT EXISTS (SELECT 1 FROM ProcessingJob WHERE GroupId = @groupId AND Status IN @active)",
            new { groupId, active = new[] { ProcessingJob.Queued, ProcessingJob.Running } });
    }

    /// <summary>
    /// Inserts the job and its files (positions from 1 in list order) and marks the group user-corrected, so a rescan
    /// keeps it, in one transaction. Returns the new job id.
    /// </summary>
    public long CreateJob(ProcessingJob job, IReadOnlyList<ProcessingJobFile> files)
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        var jobId = connection.ExecuteScalar<long>(
            """
            INSERT INTO ProcessingJob (GroupId, Title, Status, CreatedAtUtc)
            VALUES (@GroupId, @Title, @Status, @CreatedAtUtc)
            RETURNING Id
            """,
            job, transaction);
        connection.Execute(
            $"INSERT INTO ProcessingJobFile ({FileColumns}) VALUES (@JobId, @AudioFileId, @Position, @SourcePath, @DestinationPath, @Status, @Error)",
            files.Select((f, i) => new
            {
                JobId = jobId, f.AudioFileId, Position = i + 1, f.SourcePath, f.DestinationPath, f.Status, f.Error,
            }),
            transaction);
        connection.Execute("UPDATE FileGroup SET UserCorrected = 1 WHERE Id = @GroupId", new { job.GroupId }, transaction);
        transaction.Commit();
        return jobId;
    }

    /// <summary>Every job, newest first, each with its files in position order.</summary>
    public IReadOnlyList<(ProcessingJob Job, IReadOnlyList<ProcessingJobFile> Files)> GetJobs()
    {
        using var connection = connections.Open();
        var jobs = connection.Query<ProcessingJob>($"SELECT {JobColumns} FROM ProcessingJob ORDER BY Id DESC").ToList();
        var files = connection.Query<ProcessingJobFile>($"SELECT {FileColumns} FROM ProcessingJobFile ORDER BY JobId, Position")
            .ToLookup(f => f.JobId);
        return jobs.Select(j => (j, (IReadOnlyList<ProcessingJobFile>)files[j.Id].ToList())).ToList();
    }

    /// <summary>The job and its files in position order; null when no job has <paramref name="id"/>.</summary>
    public (ProcessingJob Job, IReadOnlyList<ProcessingJobFile> Files)? GetJob(long id)
    {
        using var connection = connections.Open();
        var job = connection.QuerySingleOrDefault<ProcessingJob>($"SELECT {JobColumns} FROM ProcessingJob WHERE Id = @id", new { id });
        if (job is null) return null;
        var files = connection.Query<ProcessingJobFile>(
            $"SELECT {FileColumns} FROM ProcessingJobFile WHERE JobId = @id ORDER BY Position", new { id }).ToList();
        return (job, files);
    }

    /// <summary>The oldest Queued job with its files in position order, or null when nothing is queued.</summary>
    public (ProcessingJob Job, IReadOnlyList<ProcessingJobFile> Files)? NextQueued()
    {
        using var connection = connections.Open();
        var id = connection.ExecuteScalar<long?>(
            "SELECT Id FROM ProcessingJob WHERE Status = @queued ORDER BY Id LIMIT 1", new { queued = ProcessingJob.Queued });
        return id is { } found ? GetJob(found) : null;
    }

    /// <summary>Saves the job's status, timestamps and error.</summary>
    public void UpdateJob(ProcessingJob job)
    {
        using var connection = connections.Open();
        connection.Execute(
            "UPDATE ProcessingJob SET Status = @Status, StartedAtUtc = @StartedAtUtc, FinishedAtUtc = @FinishedAtUtc, Error = @Error WHERE Id = @Id",
            job);
    }

    /// <summary>Saves the file's status and error.</summary>
    public void UpdateFile(ProcessingJobFile file)
    {
        using var connection = connections.Open();
        connection.Execute(
            "UPDATE ProcessingJobFile SET Status = @Status, Error = @Error WHERE JobId = @JobId AND Position = @Position", file);
    }

    /// <summary>Fails every Running job: at startup nothing can still be processing, so it was cut off by a restart.</summary>
    public void FailInterruptedJobs(string message, string finishedAt)
    {
        using var connection = connections.Open();
        connection.Execute(
            "UPDATE ProcessingJob SET Status = @failed, Error = @message, FinishedAtUtc = @finishedAt WHERE Status = @running",
            new { failed = ProcessingJob.Failed, running = ProcessingJob.Running, message, finishedAt });
    }
}
