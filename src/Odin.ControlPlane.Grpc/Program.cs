using Odin.ControlPlane.Grpc.Services;
using Odin.ExecutionEngine.History;
using Odin.ExecutionEngine.Matching;
using Odin.Persistence;
using Odin.Persistence.Interfaces;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddGrpcReflection();

builder.Services.AddPersistence(options =>
{
    builder.Configuration.GetSection("Persistence").Bind(options);

    if (options.Provider == PersistenceProvider.PostgreSql)
    {
        options.ConnectionString ??= builder.Configuration.GetConnectionString("OdinDatabase")
            ?? "Server=localhost;Database=odin;User Id=odin;Password=odin;";
    }
});

var hostIdentity = $"{Environment.MachineName}-{Guid.NewGuid().ToString()[..8]}";

builder.Services.AddSingleton<IHistoryService>(sp =>
    new HistoryService(
        sp.GetRequiredService<IHistoryRepository>(),
        sp.GetRequiredService<IShardRepository>(),
        sp.GetRequiredService<ILogger<HistoryService>>(),
        hostIdentity));

builder.Services.AddSingleton<IMatchingService>(sp =>
    new MatchingService(
        sp.GetRequiredService<ITaskQueueRepository>(),
        sp.GetRequiredService<ILogger<MatchingService>>()));

var otelSection = builder.Configuration.GetSection("OpenTelemetry");
var otlpEndpoint = otelSection.GetValue<string?>("Endpoint");
var serviceName = otelSection.GetValue<string?>("ServiceName") ?? "odin-control-plane-grpc";
var serviceVersion = otelSection.GetValue<string?>("ServiceVersion") ?? "1.0.0";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)
                }))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddRuntimeInstrumentation();
        metrics.AddPrometheusExporter();
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGrpcService<WorkflowServiceImpl>();
app.MapPrometheusScrapingEndpoint();

app.MapGet("/", () =>
    "Odin Control Plane gRPC service. Use a gRPC client to interact with the WorkflowService.");

app.Run();

namespace Odin.ControlPlane.Grpc
{
    public partial class Program
    {
    }
}
