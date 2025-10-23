using System;
using Hugo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core;
using static Hugo.Functional;
using static Hugo.Go;

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

        var executionResult = await Go.Ok(task)
            .Then(ResolveRegistration)
            .ThenAsync((context, ct) => ExecuteWithRegistrationAsync(context.Task, context.Registration, ct), cancellationToken)
            .ConfigureAwait(false);

        return executionResult.OnFailure(error =>
        {
            if (string.Equals(error.Code, OdinErrorCodes.WorkflowNotFound, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Workflow type {WorkflowType} not registered",
                    task.WorkflowType);
            }
        });
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

    private Result<(WorkflowTask Task, WorkflowRegistration Registration)> ResolveRegistration(WorkflowTask task) =>
        _registry.TryGetRegistration(task.WorkflowType, out var registration) && registration is not null
            ? Result.Ok((task, registration))
            : Result.Fail<(WorkflowTask, WorkflowRegistration)>(OdinErrors.WorkflowNotFound(task.WorkflowType));

    private async Task<Result<object?>> ExecuteWithRegistrationAsync(
        WorkflowTask task,
        WorkflowRegistration registration,
        CancellationToken cancellationToken)
    {
        var captured = await Result
            .TryAsync(async ct =>
            {
                using var scope = _scopeFactory.CreateScope();
                var services = scope.ServiceProvider;
                var options = BuildOptions(task);

                return await registration.Executor(services, options, task.Input, ct).ConfigureAwait(false);
            }, cancellationToken, ex =>
            {
                if (ex is OperationCanceledException)
                {
                    return Error.Canceled(token: cancellationToken);
                }

                return Error.FromException(ex)
                    .WithMetadata("workflowId", task.WorkflowId)
                    .WithMetadata("runId", task.RunId.ToString());
            })
            .ConfigureAwait(false);

        return captured
            .Then(static result => result)
            .OnFailure(error =>
            {
                if (!error.TryGetMetadata("cancellationToken", out object? _))
                {
                    _logger.LogError(
                        "Workflow execution failed for {WorkflowId}/{RunId}: {Error}",
                        task.WorkflowId,
                        task.RunId,
                        error.Message);
                }
            });
    }
}
