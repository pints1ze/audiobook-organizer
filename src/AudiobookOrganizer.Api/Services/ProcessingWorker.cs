using AudiobookOrganizer.Api.Data;

namespace AudiobookOrganizer.Api.Services;

/// <summary>
/// Runs queued processing jobs one at a time in the background. It drains the queue when the application starts and
/// again whenever <see cref="Wake"/> is called; a wake during a drain makes it check the queue once more afterwards.
/// </summary>
public class ProcessingWorker(ProcessingService processing, ProcessingRepository jobs, ILogger<ProcessingWorker> logger)
    : BackgroundService
{
    // Starts signalled so the first pass drains whatever was queued before startup. Holds at most one wake.
    private readonly SemaphoreSlim _wake = new(1, 1);
    private readonly Lock _lock = new();

    /// <summary>Asks the worker to check the queue. Wakes that arrive before it gets to it are merged into one.</summary>
    public void Wake()
    {
        lock (_lock)
        {
            if (_wake.CurrentCount == 0) _wake.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up application startup with the first drain.
        await Task.Yield();
        while (true)
        {
            try
            {
                await _wake.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Drain(stoppingToken);
        }
    }

    private void Drain(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            long jobId;
            try
            {
                if (jobs.NextQueued() is not { } next) return;
                jobId = next.Job.Id;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't read the processing queue");
                return;
            }

            try
            {
                processing.Run(jobId, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Processing job {JobId} failed unexpectedly", jobId);
                try
                {
                    processing.Fail(jobId, $"Processing failed unexpectedly: {ex.Message}");
                }
                catch (Exception failEx)
                {
                    // Stop this pass: the job may still be Queued, and retrying it at once would loop.
                    logger.LogError(failEx, "Couldn't mark processing job {JobId} as failed", jobId);
                    return;
                }
            }
        }
    }
}
