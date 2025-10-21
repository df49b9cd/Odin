using Hugo;
using Odin.Core;
using Odin.ExecutionEngine.History;
using Odin.ExecutionEngine.Matching;
using Odin.Persistence;
using Odin.Persistence.Interfaces;
using Odin.Persistence.Repositories;
using Npgsql;
using System.Data;
using static Hugo.Go;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// Add OpenAPI support (using built-in .NET support)
builder.Services.AddOpenApi();

// Configure connection strings
var connectionString = builder.Configuration.GetConnectionString("OdinDatabase")
    ?? "Server=localhost;Database=odin;User Id=odin;Password=odin;";

// Register connection factory
builder.Services.AddSingleton<IDbConnectionFactory>(sp =>
    new SimpleDbConnectionFactory(connectionString));

// Register repositories (using stubs for Phase 1)
builder.Services.AddSingleton<INamespaceRepository>(sp =>
    new NamespaceRepository(
        sp.GetRequiredService<IDbConnectionFactory>(),
        sp.GetRequiredService<ILogger<NamespaceRepository>>()));

builder.Services.AddSingleton<IWorkflowExecutionRepository>(sp =>
    new WorkflowExecutionRepository());

builder.Services.AddSingleton<IHistoryRepository>(sp =>
    new InMemoryHistoryRepository());

builder.Services.AddSingleton<IShardRepository>(sp =>
    new InMemoryShardRepository());

builder.Services.AddSingleton<ITaskQueueRepository>(sp =>
    new InMemoryTaskQueueRepository());

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

// Placeholder repositories for Phase 1
internal sealed class InMemoryHistoryRepository : IHistoryRepository
{
    public Task<Result<Unit>> AppendEventsAsync(
        string namespaceId,
        string workflowId,
        string runId,
        IReadOnlyList<Odin.Contracts.HistoryEvent> events,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<Odin.Contracts.WorkflowHistoryBatch>> GetHistoryAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long fromEventId,
        int maxEvents,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(new Odin.Contracts.WorkflowHistoryBatch
        {
            NamespaceId = Guid.TryParse(namespaceId, out var nsId) ? nsId : Guid.Empty,
            WorkflowId = workflowId,
            RunId = Guid.TryParse(runId, out var rId) ? rId : Guid.Empty,
            Events = new List<Odin.Contracts.HistoryEvent>(),
            FirstEventId = 1,
            LastEventId = 0
        }));
    }

    public Task<Result<IReadOnlyList<Odin.Contracts.HistoryEvent>>> GetEventsFromAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long fromEventId,
        int maxEvents = 1000,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok<IReadOnlyList<Odin.Contracts.HistoryEvent>>(
            new List<Odin.Contracts.HistoryEvent>()));
    }

    public Task<Result<Odin.Contracts.HistoryEvent>> GetEventAsync(
        string namespaceId,
        string workflowId,
        string runId,
        long eventId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Fail<Odin.Contracts.HistoryEvent>(
            Error.From("Not found", "NOT_FOUND")));
    }

    public Task<Result<long>> GetEventCountAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(0L));
    }

    public Task<Result<int>> ArchiveOldEventsAsync(
        string namespaceId,
        DateTimeOffset olderThan,
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(0));
    }

    public Task<Result<bool>> ValidateEventSequenceAsync(
        string namespaceId,
        string workflowId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(true));
    }
}

internal sealed class InMemoryShardRepository : IShardRepository
{
    public Task<Result<ShardLease>> AcquireLeaseAsync(
        int shardId,
        string ownerHost,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(new ShardLease(
            shardId,
            ownerHost,
            DateTimeOffset.UtcNow.Add(leaseDuration),
            0,
            long.MaxValue)));
    }

    public Task<Result<ShardLease>> RenewLeaseAsync(
        int shardId,
        string ownerHost,
        TimeSpan extendBy,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(new ShardLease(
            shardId,
            ownerHost,
            DateTimeOffset.UtcNow.Add(extendBy),
            0,
            long.MaxValue)));
    }

    public Task<Result<Unit>> ReleaseLeaseAsync(
        int shardId,
        string ownerHost,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<ShardLease?>> GetLeaseAsync(
        int shardId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok<ShardLease?>(new ShardLease(
            shardId,
            "localhost",
            DateTimeOffset.UtcNow.AddMinutes(5),
            0,
            long.MaxValue)));
    }

    public Task<Result<IReadOnlyList<int>>> GetOwnedShardsAsync(
        string ownerHost,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok<IReadOnlyList<int>>(new List<int>()));
    }

    public Task<Result<IReadOnlyList<ShardLease>>> ListAllShardsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok<IReadOnlyList<ShardLease>>(new List<ShardLease>()));
    }

    public Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(0));
    }

    public Task<Result<Unit>> InitializeShardsAsync(
        int shardCount,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(Unit.Value));
    }
}

internal sealed class InMemoryTaskQueueRepository : ITaskQueueRepository
{
    public Task<Result<Guid>> EnqueueAsync(
        Odin.Contracts.TaskQueueItem task,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(Guid.NewGuid()));
    }

    public Task<Result<Odin.Contracts.TaskLease?>> PollAsync(
        string queueName,
        string workerIdentity,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok<Odin.Contracts.TaskLease?>(null));
    }

    public Task<Result<Odin.Contracts.TaskLease>> HeartbeatAsync(
        Guid leaseId,
        TimeSpan extendBy,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Fail<Odin.Contracts.TaskLease>(
            Error.From("Not found", "NOT_FOUND")));
    }

    public Task<Result<Unit>> CompleteAsync(
        Guid taskId,
        Guid leaseId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<Unit>> FailAsync(
        Guid taskId,
        Guid leaseId,
        string reason,
        bool requeue = true,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(Unit.Value));
    }

    public Task<Result<int>> GetQueueDepthAsync(
        string queueName,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(0));
    }

    public Task<Result<Dictionary<string, int>>> ListQueuesAsync(
        string? namespaceId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(new Dictionary<string, int>()));
    }

    public Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(0));
    }

    public Task<Result<int>> PurgeOldTasksAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Ok(0));
    }
}

// Simple connection factory for PostgreSQL
internal sealed class SimpleDbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SimpleDbConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DatabaseProvider Provider => DatabaseProvider.PostgreSQL;

    public async Task<Result<IDbConnection>> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return Result.Ok<IDbConnection>(connection);
        }
        catch (Exception ex)
        {
            return Result.Fail<IDbConnection>(Error.From($"Failed to create database connection: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return Result.Ok(true);
        }
        catch (Exception ex)
        {
            return Result.Fail<bool>(Error.From($"Database connection test failed: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }
}
