using System.Text.Json;
using Odin.Contracts;
using Odin.Core;
using Odin.ExecutionEngine.Matching;
using Odin.Sdk;

namespace Odin.ExecutionEngine.SystemWorkers.Services;

public sealed class SystemTaskSeeder(
    IMatchingService matchingService,
    ILogger<SystemTaskSeeder> logger) : BackgroundService
{
    private static readonly Guid NamespaceId = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private readonly JsonSerializerOptions _serializerOptions = JsonOptions.Default;
    private long _taskId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queues = new[] { "system:timers", "system:retries", "system:cleanup" };

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var queue in queues)
            {
                var id = Interlocked.Increment(ref _taskId);
                var runId = Guid.NewGuid();
                var workflowType = queue switch
                {
                    "system:timers" => "system.timer",
                    "system:retries" => "system.retry",
                    "system:cleanup" => "system.cleanup",
                    _ => "system.unknown"
                };

                var taskPayload = new WorkflowTask(
                    Namespace: "system",
                    WorkflowId: $"SYS-{workflowType}-{id:0000}",
                    RunId: runId.ToString("N"),
                    TaskQueue: queue,
                    WorkflowType: workflowType,
                    Input: new { id, createdAt = DateTimeOffset.UtcNow },
                    Metadata: new Dictionary<string, string>
                    {
                        ["system"] = "true"
                    },
                    StartedAt: DateTimeOffset.UtcNow);

                var queueItem = new TaskQueueItem
                {
                    NamespaceId = NamespaceId,
                    TaskQueueName = queue,
                    TaskQueueType = TaskQueueType.Workflow,
                    TaskId = id,
                    WorkflowId = taskPayload.WorkflowId,
                    RunId = runId,
                    ScheduledAt = DateTimeOffset.UtcNow,
                    ExpiryAt = null,
                    TaskData = JsonSerializer.SerializeToDocument(taskPayload, _serializerOptions),
                    PartitionHash = HashingUtilities.CalculatePartitionHash(queue)
                };

                var result = await matchingService.EnqueueTaskAsync(queueItem, stoppingToken).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    logger.LogDebug("Seeded system task {WorkflowId} for queue {Queue}", taskPayload.WorkflowId, queue);
                }
                else
                {
                    logger.LogWarning("Failed to seed system task for queue {Queue}: {Error}", queue, result.Error?.Message);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
