using Hugo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core;

namespace Odin.Sdk;

public sealed class WorkflowExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowRegistry _registry;
    private readonly ILogger<WorkflowExecutor> _logger;

    public WorkflowExecutor(
        IServiceScopeFactory scopeFactory,
        IWorkflowRegistry registry,
        ILogger<WorkflowExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
    }

    public async Task<Result<object?>> ExecuteAsync(
        WorkflowTask task,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!_registry.TryGetRegistration(task.WorkflowType, out var registration) || registration is null)
        {
            return Result.Fail<object?>(OdinErrors.WorkflowNotFound(task.WorkflowType));
        }

        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var options = BuildOptions(task);

        try
        {
            return await registration.Executor(services, options, task.Input, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result.Fail<object?>(Error.Canceled(token: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to execute workflow {WorkflowId}/{RunId}",
                task.WorkflowId,
                task.RunId);

            return Result.Fail<object?>(Error.FromException(ex));
        }
    }

    private static WorkflowRuntimeOptions BuildOptions(WorkflowTask task)
    {
        return new WorkflowRuntimeOptions
        {
            Namespace = task.Namespace,
            WorkflowId = task.WorkflowId,
            RunId = task.RunId,
            TaskQueue = task.TaskQueue,
            ScheduleId = string.Empty,
            ScheduleGroup = string.Empty,
            Metadata = task.Metadata is { Count: > 0 } meta
                ? new Dictionary<string, string>(meta)
                : new Dictionary<string, string>(),
            StartedAt = task.StartedAt,
            InitialLogicalClock = task.InitialLogicalClock,
            ReplayCount = task.ReplayCount,
            StateStore = task.StateStore,
            TimeProvider = task.TimeProvider ?? TimeProvider.System,
            SerializerOptions = JsonOptions.Default
        };
    }
}
