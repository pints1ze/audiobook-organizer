using AudiobookOrganizer.Api.Configuration;
using AudiobookOrganizer.Api.Data;
using Microsoft.Extensions.Options;

namespace AudiobookOrganizer.Api.Services;

/// <summary>
/// Searches the catalog in the background for every book with no stored search, no chosen match and no processing
/// job, one at a time with <see cref="CandidateSearchSettings.DelayMs"/> between two searches, and stores the
/// candidates. Runs when the application starts and again whenever <see cref="Wake"/> is called (after each completed
/// scan); a wake during a pass makes it check once more afterwards. Never applies a match.
/// </summary>
public class CandidateSearchWorker(
    IServiceProvider services,
    CandidateRepository candidates,
    IOptions<CandidateSearchSettings> settings,
    ILogger<CandidateSearchWorker> logger)
    : BackgroundService
{
    // Starts signalled so the first pass searches whatever was left unsearched before startup. Holds at most one wake.
    private readonly SemaphoreSlim _wake = new(1, 1);
    private readonly Lock _lock = new();

    /// <summary>Asks the worker to look for unsearched books. Wakes that arrive before it gets to it are merged into one.</summary>
    public void Wake()
    {
        lock (_lock)
        {
            if (_wake.CurrentCount == 0) _wake.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up application startup with the first pass.
        await Task.Yield();
        while (true)
        {
            try
            {
                await _wake.WaitAsync(stoppingToken);
                await SearchAll(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task SearchAll(CancellationToken stoppingToken)
    {
        IReadOnlyList<long> groupIds;
        try
        {
            groupIds = candidates.GetUnsearchedGroupIds();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't read the books waiting for a catalog search");
            return;
        }

        for (var i = 0; i < groupIds.Count; i++)
        {
            if (i > 0) await Task.Delay(settings.Value.DelayMs, stoppingToken);
            var groupId = groupIds[i];
            try
            {
                // Transient, and holds typed HttpClients: resolve a fresh one per search.
                var service = services.GetRequiredService<CandidateService>();
                if (await service.RefreshAsync(groupId, stoppingToken) is null) continue;
                logger.LogInformation("Searched the catalog for group {GroupId}", groupId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The background catalog search for group {GroupId} failed", groupId);
            }
        }
    }
}
