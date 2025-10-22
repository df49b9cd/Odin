using Odin.ExecutionEngine.Matching;
using Odin.Sdk;
using OrderProcessing.Shared;

namespace Odin.WorkerHost;

public sealed class Worker(
    IMatchingService matchingService,
    WorkflowExecutor executor,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerIdentity = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";
        await using var subscription = await matchingService.SubscribeAsync("orders", workerIdentity, stoppingToken).ConfigureAwait(false);

        await foreach (var task in subscription.Reader.ReadAllAsync(stoppingToken))
        {
            logger.LogInformation(
                "Processing workflow {WorkflowId} (RunId: {RunId})",
                task.WorkflowTask.WorkflowId,
                task.WorkflowTask.RunId);

            var result = await executor.ExecuteAsync(task.WorkflowTask, stoppingToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                await task.CompleteAsync(stoppingToken).ConfigureAwait(false);

                if (result.Value is OrderResult order)
                {
                    logger.LogInformation(
                        "Completed order {OrderId} with transaction {TransactionId}",
                        order.OrderId,
                        order.TransactionId);
                }
            }
            else
            {
                var reason = result.Error?.Message ?? "Unknown failure";
                logger.LogWarning(
                    "Workflow {WorkflowId} failed: {Error}",
                    task.WorkflowTask.WorkflowId,
                    reason);

                await task.FailAsync(reason, requeue: false, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
