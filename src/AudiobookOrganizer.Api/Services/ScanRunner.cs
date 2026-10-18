using System.Diagnostics.CodeAnalysis;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Local;

namespace AudiobookOrganizer.Api.Services;

/// <summary>
/// Runs at most one scan at a time on a background task, independent of the HTTP request that started it.
/// The scan stops when the application shuts down; a run cut off by a crash is failed at the next startup.
/// </summary>
public class ScanRunner(
    ScanService scans,
    ScanRepository repository,
    IHostApplicationLifetime lifetime,
    ILogger<ScanRunner> logger)
{
    private readonly Lock _lock = new();
    private Task _current = Task.CompletedTask;

    /// <summary>Records a new Running run and starts it, or returns false if a scan is already running.</summary>
    public bool TryStart([NotNullWhen(true)] out ScanRun? run)
    {
        lock (_lock)
        {
            if (!_current.IsCompleted)
            {
                run = null;
                return false;
            }

            run = scans.Start();
            // The background task works on its own copy, so the caller's snapshot stays "Running".
            var working = repository.GetRun(run.Id)!;
            _current = Task.Run(() => Execute(working));
            return true;
        }
    }

    private void Execute(ScanRun run)
    {
        try
        {
            scans.Execute(run, lifetime.ApplicationStopping);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scan {ScanId} failed unexpectedly", run.Id);
            try
            {
                run.Status = ScanRun.Failed;
                run.Error = $"The scan failed unexpectedly: {ex.Message}";
                run.FinishedAt = DateTime.UtcNow.ToString("O");
                repository.UpdateRun(run);
            }
            catch (Exception updateEx)
            {
                logger.LogError(updateEx, "Couldn't mark scan {ScanId} as failed", run.Id);
            }
        }
    }
}
