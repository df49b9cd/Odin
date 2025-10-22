using Odin.Sdk;
using Odin.WorkerHost.Infrastructure;
using OrderProcessing.Shared;

namespace Odin.WorkerHost.Services;

public sealed class OrderTaskSeeder(
    IWorkflowTaskQueue taskQueue,
    ILogger<OrderTaskSeeder> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var counter = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            counter++;
            var orderId = $"ORD-{counter:0000}";

            var task = new WorkflowTask(
                Namespace: "default",
                WorkflowId: orderId,
                RunId: Guid.NewGuid().ToString("N"),
                TaskQueue: "orders",
                WorkflowType: "order-processing",
                Input: new OrderRequest(orderId, Amount: Random.Shared.Next(25, 150), CustomerId: "cust-odin"),
                Metadata: new Dictionary<string, string>
                {
                    ["customer"] = "odin",
                    ["priority"] = counter % 3 == 0 ? "high" : "normal"
                },
                StartedAt: DateTimeOffset.UtcNow);

            await taskQueue.EnqueueAsync(task, stoppingToken).ConfigureAwait(false);

            logger.LogInformation("Queued workflow task {OrderId}", orderId);

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }
}
