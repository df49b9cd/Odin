using Odin.ExecutionEngine.Matching;

namespace Odin.ExecutionEngine.SystemWorkers.Services;

public sealed class RetryWorker(
    IMatchingService matchingService,
    ILogger<RetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = $"retry-worker-{Environment.MachineName}-{Guid.NewGuid():N}";
        await using var subscription = await matchingService.SubscribeAsync("system:retries", identity, stoppingToken).ConfigureAwait(false);

        await foreach (var task in subscription.Reader.ReadAllAsync(stoppingToken))
        {
            logger.LogInformation(
                "Processing retry for workflow {WorkflowId}",
                task.WorkflowTask.WorkflowId);

            var result = await task.FailAsync("retry-scheduled", requeue: true, stoppingToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Failed to reschedule workflow {WorkflowId}: {Error}",
                    task.WorkflowTask.WorkflowId,
                    result.Error?.Message);
            }
        }
    }
}
