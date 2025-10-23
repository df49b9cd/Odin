using System.Net.Http;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Odin.ControlPlane.Grpc;
using Odin.ExecutionEngine.History;
using Odin.ExecutionEngine.Matching;
using Odin.Persistence;
using Odin.Persistence.Interfaces;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// Add OpenAPI support (using built-in .NET support)
builder.Services.AddOpenApi();

// Register persistence infrastructure (configurable provider)
builder.Services.AddPersistence(options =>
{
    builder.Configuration.GetSection("Persistence").Bind(options);

    if (options.Provider == PersistenceProvider.PostgreSql)
    {
        options.ConnectionString ??= builder.Configuration.GetConnectionString("OdinDatabase")
            ?? "Server=localhost;Database=odin;User Id=odin;Password=odin;";
    }
});

// Register services
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

// gRPC WorkflowService client facade
builder.Services.AddGrpcClient<WorkflowService.WorkflowServiceClient>(options =>
{
    var endpoint = builder.Configuration.GetValue<string?>("Grpc:WorkflowService:Address")
        ?? "http://localhost:7233";
    options.Address = new Uri(endpoint);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    EnableMultipleHttp2Connections = true,
    KeepAlivePingDelay = TimeSpan.FromSeconds(60),
    KeepAlivePingTimeout = TimeSpan.FromSeconds(30)
});

var otelSection = builder.Configuration.GetSection("OpenTelemetry");
var otlpEndpoint = otelSection.GetValue<string?>("Endpoint");
var serviceName = otelSection.GetValue<string?>("ServiceName") ?? "odin-control-plane-api";
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
        metrics.AddHttpClientInstrumentation();
        metrics.AddRuntimeInstrumentation();
        metrics.AddPrometheusExporter();
    });

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure JSON options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // OpenAPI endpoint at /openapi/v1.json
}

app.UseHttpsRedirection();
app.UseCors();

app.MapControllers();
app.MapPrometheusScrapingEndpoint();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0-phase1"
}))
.WithName("HealthCheck")
.WithTags("Health");

app.Run();
