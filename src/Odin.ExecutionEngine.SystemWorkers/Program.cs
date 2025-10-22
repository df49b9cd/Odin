using Microsoft.Extensions.Logging;
using Odin.ExecutionEngine.Matching;
using Odin.ExecutionEngine.SystemWorkers.Services;
using Odin.Persistence.InMemory;
using Odin.Persistence.Interfaces;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddSimpleConsole(options => options.TimestampFormat = "HH:mm:ss ");
});

builder.Services.AddSingleton<InMemoryTaskQueueRepository>();
builder.Services.AddSingleton<ITaskQueueRepository>(sp => sp.GetRequiredService<InMemoryTaskQueueRepository>());
builder.Services.AddSingleton<IMatchingService, MatchingService>();

builder.Services.AddHostedService<SystemTaskSeeder>();
builder.Services.AddHostedService<TimerWorker>();
builder.Services.AddHostedService<RetryWorker>();
builder.Services.AddHostedService<CleanupWorker>();

var host = builder.Build();
host.Run();
