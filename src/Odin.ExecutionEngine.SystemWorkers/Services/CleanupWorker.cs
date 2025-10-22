using Odin.ExecutionEngine.Matching;
using Odin.Persistence.Interfaces;

namespace Odin.ExecutionEngine.SystemWorkers.Services;

public sealed class CleanupWorker(
    IMatchingService matchingService,
    ITaskQueueRepository taskQueueRepository,
    ILogger<CleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var reclaimed = await matchingService.ReclaimExpiredLeasesAsync(stoppingToken).ConfigureAwait(false);
            if (reclaimed.IsSuccess && reclaimed.Value > 0)
            {
                logger.LogInformation("Reclaimed {Count} expired leases", reclaimed.Value);
            }

            var purgeResult = await taskQueueRepository.PurgeOldTasksAsync(DateTimeOffset.UtcNow.AddMinutes(-5), stoppingToken).ConfigureAwait(false);
            if (purgeResult.IsSuccess && purgeResult.Value > 0)
            {
                logger.LogInformation("Purged {Count} stale tasks", purgeResult.Value);
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
