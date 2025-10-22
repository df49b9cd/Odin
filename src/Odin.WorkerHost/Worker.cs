using Odin.Sdk;
using Odin.WorkerHost.Infrastructure;

namespace Odin.WorkerHost;

public sealed class Worker(
    IWorkflowTaskQueue taskQueue,
    WorkflowExecutor executor,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker host started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            WorkflowTask task;

            try
            {
                task = await taskQueue.DequeueAsync(stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            logger.LogInformation(
                "Processing workflow {WorkflowId}/{RunId} ({WorkflowType})",
                task.WorkflowId,
                task.RunId,
                task.WorkflowType);

            var result = await executor.ExecuteAsync(task, stoppingToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "Workflow {WorkflowId} completed successfully.",
                    task.WorkflowId);
            }
            else
            {
                logger.LogError(
                    "Workflow {WorkflowId} failed: {Error}",
                    task.WorkflowId,
                    result.Error?.Message);
            }
        }

        logger.LogInformation("Worker host stopping.");
    }
}
