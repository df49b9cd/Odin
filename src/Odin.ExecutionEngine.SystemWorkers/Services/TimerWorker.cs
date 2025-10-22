using Odin.ExecutionEngine.Matching;

namespace Odin.ExecutionEngine.SystemWorkers.Services;

public sealed class TimerWorker(
    IMatchingService matchingService,
    ILogger<TimerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = $"timer-worker-{Environment.MachineName}-{Guid.NewGuid():N}";
        await using var subscription = await matchingService.SubscribeAsync("system:timers", identity, stoppingToken).ConfigureAwait(false);

        await foreach (var task in subscription.Reader.ReadAllAsync(stoppingToken))
        {
            logger.LogInformation(
                "Firing timer for workflow {WorkflowId} (RunId: {RunId})",
                task.WorkflowTask.WorkflowId,
                task.WorkflowTask.RunId);

            var result = await task.CompleteAsync(stoppingToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Failed to complete timer task {WorkflowId}: {Error}",
                    task.WorkflowTask.WorkflowId,
                    result.Error?.Message);
            }
        }
    }
}
