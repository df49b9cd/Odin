using Microsoft.Extensions.Logging;
using Odin.ExecutionEngine.Matching;
using Odin.Persistence.InMemory;
using Odin.Persistence.Interfaces;
using Odin.Sdk;
using Odin.WorkerHost;
using Odin.WorkerHost.Services;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Activities;
using OrderProcessing.Shared.Workflows;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddSimpleConsole(static options =>
    {
        options.TimestampFormat = "HH:mm:ss ";
    });
});

builder.Services.AddSingleton<InMemoryTaskQueueRepository>();
builder.Services.AddSingleton<ITaskQueueRepository>(sp => sp.GetRequiredService<InMemoryTaskQueueRepository>());
builder.Services.AddSingleton<IMatchingService, MatchingService>();

builder.Services.AddWorkflowRuntime();
builder.Services.AddWorkflow<OrderProcessingWorkflow, OrderRequest, OrderResult>("order-processing");
builder.Services.AddTransient<ProcessPaymentActivity>();

builder.Services.AddHostedService<OrderTaskSeeder>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
