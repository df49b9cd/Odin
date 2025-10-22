using Hugo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Sdk;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Activities;
using OrderProcessing.Shared.Workflows;

Console.WriteLine("Odin - Order Processing Sample");
Console.WriteLine("================================\n");

var services = new ServiceCollection();
services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");
});
services.AddWorkflowRuntime();
services.AddWorkflow<OrderProcessingWorkflow, OrderRequest, OrderResult>("order-processing");
services.AddTransient<ProcessPaymentActivity>();

await using var provider = services.BuildServiceProvider();
var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Sample");
var executor = provider.GetRequiredService<WorkflowExecutor>();
var stateStore = new InMemoryDeterministicStateStore();

var workflowId = "sample-order";
var runId = Guid.NewGuid().ToString("N");
var request = new OrderRequest(workflowId, Amount: 99.95m, CustomerId: "cust-0001");

var firstTask = new WorkflowTask(
    Namespace: "default",
    WorkflowId: workflowId,
    RunId: runId,
    TaskQueue: "orders",
    WorkflowType: "order-processing",
    Input: request,
    Metadata: new Dictionary<string, string> { ["customer"] = request.CustomerId },
    StateStore: stateStore);

var firstResult = await executor.ExecuteAsync(firstTask, CancellationToken.None);
LogResult("Initial execution", firstResult, logger);

var replayTask = firstTask with { ReplayCount = 1 };
var replayResult = await executor.ExecuteAsync(replayTask, CancellationToken.None);
LogResult("Deterministic replay", replayResult, logger);

Console.WriteLine();
Console.WriteLine("Press any key to exit...");
Console.ReadKey();

static void LogResult(string stage, Result<object?> result, ILogger logger)
{
    if (result.IsSuccess && result.Value is OrderResult order)
    {
        logger.LogInformation(
            "{Stage}: Order {OrderId} completed with transaction {TransactionId} (amount: {Amount})",
            stage,
            order.OrderId,
            order.TransactionId,
            order.Amount);
    }
    else
    {
        logger.LogError(
            "{Stage}: Workflow failed - {Error}",
            stage,
            result.Error?.Message ?? "unknown error");
    }
}
