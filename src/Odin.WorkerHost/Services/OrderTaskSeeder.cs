using System.Text.Json;
using Odin.Contracts;
using Odin.Core;
using Odin.ExecutionEngine.Matching;
using Odin.Sdk;
using OrderProcessing.Shared;

namespace Odin.WorkerHost.Services;

public sealed class OrderTaskSeeder(
    IMatchingService matchingService,
    ILogger<OrderTaskSeeder> logger) : BackgroundService
{
    private const string QueueName = "orders";
    private static readonly Guid NamespaceId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private long _taskId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serializerOptions = JsonOptions.Default;

        while (!stoppingToken.IsCancellationRequested)
        {
            var orderId = $"ORD-{Interlocked.Increment(ref _taskId):0000}";
            var runId = Guid.NewGuid();

            var workflowTask = new WorkflowTask(
                Namespace: "default",
                WorkflowId: orderId,
                RunId: runId.ToString("N"),
                TaskQueue: QueueName,
                WorkflowType: "order-processing",
                Input: new OrderRequest(orderId, Amount: Random.Shared.Next(50, 250), CustomerId: "cust-odin"),
                Metadata: new Dictionary<string, string>
                {
                    ["customer"] = "odin",
                    ["priority"] = _taskId % 3 == 0 ? "high" : "normal"
                },
                StartedAt: DateTimeOffset.UtcNow);

            var taskQueueItem = new TaskQueueItem
            {
                NamespaceId = NamespaceId,
                TaskQueueName = QueueName,
                TaskQueueType = TaskQueueType.Workflow,
                TaskId = _taskId,
                WorkflowId = orderId,
                RunId = runId,
                ScheduledAt = DateTimeOffset.UtcNow,
                ExpiryAt = null,
                TaskData = JsonSerializer.SerializeToDocument(workflowTask, serializerOptions),
                PartitionHash = HashingUtilities.CalculatePartitionHash(QueueName)
            };

            var enqueueResult = await matchingService.EnqueueTaskAsync(taskQueueItem, stoppingToken).ConfigureAwait(false);

            if (enqueueResult.IsSuccess)
            {
                logger.LogInformation("Queued workflow task {OrderId} (TaskId: {TaskId})", orderId, enqueueResult.Value);
            }
            else
            {
                logger.LogWarning(
                    "Failed to queue workflow task {OrderId}: {Error}",
                    orderId,
                    enqueueResult.Error?.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }
}
