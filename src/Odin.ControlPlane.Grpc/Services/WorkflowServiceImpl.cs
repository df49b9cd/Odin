using System;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hugo;
using Odin.Contracts;
using Odin.ControlPlane.Grpc;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Functional;
using static Hugo.Go;
using DomainWorkflowExecution = Odin.Contracts.WorkflowExecution;
using DomainWorkflowState = Odin.Contracts.WorkflowState;
using ProtoWorkflowExecution = Odin.ControlPlane.Grpc.WorkflowExecution;
using WorkflowStateProto = Odin.ControlPlane.Grpc.WorkflowState;

namespace Odin.ControlPlane.Grpc.Services;

/// <summary>
/// Implements workflow lifecycle operations over gRPC.
/// </summary>
public sealed class WorkflowServiceImpl(
    IWorkflowExecutionRepository workflowRepository,
    ILogger<WorkflowServiceImpl> logger) : WorkflowService.WorkflowServiceBase
{
    private readonly IWorkflowExecutionRepository _workflowRepository = workflowRepository;
    private readonly ILogger<WorkflowServiceImpl> _logger = logger;

    /// <inheritdoc />
    public override async Task<StartWorkflowResponse> StartWorkflow(
        StartWorkflowRequest request,
        ServerCallContext context)
    {
        var stateResult = Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.WorkflowType),
                static _ => Error.From("Workflow type is required", "INVALID_ARGUMENT"))
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.TaskQueue),
                static _ => Error.From("Task queue is required", "INVALID_ARGUMENT"))
            .Then(r => ParseNamespaceId(r.NamespaceId)
                .Map(namespaceId => (Request: r, NamespaceId: namespaceId)))
            .Map(state =>
            {
                var workflowId = string.IsNullOrWhiteSpace(state.Request.WorkflowId)
                    ? Guid.NewGuid().ToString()
                    : state.Request.WorkflowId;
                var runId = Guid.NewGuid();
                var now = DateTimeOffset.UtcNow;

                var execution = new DomainWorkflowExecution
                {
                    NamespaceId = state.NamespaceId,
                    WorkflowId = workflowId,
                    RunId = runId,
                    WorkflowType = state.Request.WorkflowType,
                    TaskQueue = state.Request.TaskQueue,
                    WorkflowState = DomainWorkflowState.Running,
                    StartedAt = now,
                    LastUpdatedAt = now,
                    ShardId = _workflowRepository.CalculateShardId(workflowId)
                };

                return new StartWorkflowState(execution, workflowId, runId);
            });

        var createResult = await stateResult
            .ThenAsync(async (state, ct) =>
            {
                var createResult = await _workflowRepository.CreateAsync(state.Execution, ct).ConfigureAwait(false);
                return createResult.Map(_ => state);
            }, context.CancellationToken)
            .ConfigureAwait(false);

        var startResult = createResult
            .OnFailure(error => _logger.LogError(
                "Failed to start workflow {WorkflowType}: {Error}",
                request.WorkflowType,
                error.Message))
            .OnSuccess(state => _logger.LogInformation(
                "Started workflow {WorkflowType} with ID {WorkflowId}/{RunId}",
                request.WorkflowType,
                state.WorkflowId,
                state.RunId))
            .Map(state => new StartWorkflowResponse
            {
                WorkflowId = state.WorkflowId,
                RunId = state.RunId.ToString()
            });

        return startResult.Match(
            response => response,
            error => throw ToRpcException(
                error,
                StatusCode.InvalidArgument,
                error.Message ?? "Failed to start workflow"));
    }

    /// <inheritdoc />
    public override async Task<GetWorkflowResponse> GetWorkflow(
        GetWorkflowRequest request,
        ServerCallContext context)
    {
        var workflowResult = await Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.WorkflowId),
                static _ => Error.From("Workflow ID is required", "INVALID_ARGUMENT"))
            .Then(r => ParseNamespaceId(r.NamespaceId)
                .Map(namespaceId => (Request: r, NamespaceId: namespaceId)))
            .ThenAsync((state, ct) => FetchWorkflowAsync(
                state.NamespaceId,
                state.Request.WorkflowId,
                state.Request.RunId,
                ct), context.CancellationToken)
            .OnFailureAsync(error => _logger.LogWarning(
                "Failed to get workflow {WorkflowId}: {Error}",
                request.WorkflowId,
                error.Message), context.CancellationToken)
            .MapAsync(execution => new GetWorkflowResponse
            {
                Execution = MapToProto(execution)
            }, context.CancellationToken)
            .ConfigureAwait(false);

        return workflowResult.Match(
            response => response,
            error => throw ToRpcException(
                error,
                StatusCode.NotFound,
                $"Workflow '{request.WorkflowId}' not found"));
    }

    /// <inheritdoc />
    public override async Task<SignalWorkflowResponse> SignalWorkflow(
        SignalWorkflowRequest request,
        ServerCallContext context)
    {
        var fetchResult = await Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.SignalName),
                static _ => Error.From("Signal name is required", "INVALID_ARGUMENT"))
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.WorkflowId),
                static _ => Error.From("Workflow ID is required", "INVALID_ARGUMENT"))
            .Then(r => ParseNamespaceId(r.NamespaceId)
                .Map(namespaceId => (Request: r, NamespaceId: namespaceId)))
            .ThenAsync((state, ct) => FetchWorkflowAsync(
                state.NamespaceId,
                state.Request.WorkflowId,
                state.Request.RunId,
                ct), context.CancellationToken)
            .ConfigureAwait(false);

        var signalResult = fetchResult
            .Ensure(
                workflow => workflow.WorkflowState == DomainWorkflowState.Running,
                workflow => Error.From(
                    $"Workflow is not running (current status: {workflow.WorkflowState})",
                    "FAILED_PRECONDITION"))
            .OnSuccess(_ => _logger.LogInformation(
                "Signal {SignalName} accepted for workflow {WorkflowId}",
                request.SignalName,
                request.WorkflowId))
            .OnFailure(error => _logger.LogWarning(
                "Failed to signal workflow {WorkflowId}: {Error}",
                request.WorkflowId,
                error.Message));

        return signalResult.Match(
            _ => new SignalWorkflowResponse(),
            error => throw ToRpcException(
                error,
                StatusCode.InvalidArgument,
                error.Message ?? "Failed to signal workflow"));
    }

    /// <inheritdoc />
    public override async Task<TerminateWorkflowResponse> TerminateWorkflow(
        TerminateWorkflowRequest request,
        ServerCallContext context)
    {
        var fetchResult = await Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.WorkflowId),
                static _ => Error.From("Workflow ID is required", "INVALID_ARGUMENT"))
            .Then(r => ParseNamespaceId(r.NamespaceId)
                .Map(namespaceId => (Request: r, NamespaceId: namespaceId)))
            .ThenAsync(async (state, ct) =>
            {
                var workflowResult = await FetchWorkflowAsync(
                    state.NamespaceId,
                    state.Request.WorkflowId,
                    state.Request.RunId,
                    ct).ConfigureAwait(false);

                return workflowResult.Map(execution => new TerminateWorkflowState(
                    state.Request,
                    state.NamespaceId,
                    execution));
            }, context.CancellationToken)
            .ConfigureAwait(false);

        var ensured = fetchResult.Ensure(state =>
                state.Execution.WorkflowState == DomainWorkflowState.Running,
            state => Error.From(
                $"Workflow is not running (current status: {state.Execution.WorkflowState})",
                "FAILED_PRECONDITION"));

        var terminated = await ensured
            .ThenAsync(async (state, ct) =>
            {
                var terminate = await _workflowRepository.TerminateAsync(
                    state.NamespaceId.ToString(),
                    state.Request.WorkflowId,
                    state.Execution.RunId.ToString(),
                    string.IsNullOrWhiteSpace(state.Request.Reason)
                        ? "Terminated via gRPC"
                        : state.Request.Reason!,
                    ct).ConfigureAwait(false);

                return terminate.Map(_ => state);
            }, context.CancellationToken)
            .ConfigureAwait(false);

        var terminateResult = terminated
            .OnSuccess(state => _logger.LogInformation(
                "Terminated workflow {WorkflowId}/{RunId}: {Reason}",
                request.WorkflowId,
                state.Execution.RunId,
                request.Reason))
            .OnFailure(error => _logger.LogError(
                "Failed to terminate workflow {WorkflowId}: {Error}",
                request.WorkflowId,
                error.Message))
            .Map(_ => new TerminateWorkflowResponse());

        return terminateResult.Match(
            response => response,
            error => throw ToRpcException(
                error,
                StatusCode.Internal,
                error.Message ?? "Failed to terminate workflow"));
    }

    /// <inheritdoc />
    public override async Task<QueryWorkflowResponse> QueryWorkflow(
        QueryWorkflowRequest request,
        ServerCallContext context)
    {
        var fetchResult = await Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.WorkflowId),
                static _ => Error.From("Workflow ID is required", "INVALID_ARGUMENT"))
            .Then(r => ParseNamespaceId(r.NamespaceId)
                .Map(namespaceId => (Request: r, NamespaceId: namespaceId)))
            .ThenAsync((state, ct) => FetchWorkflowAsync(
                state.NamespaceId,
                state.Request.WorkflowId,
                state.Request.RunId,
                ct), context.CancellationToken)
            .ConfigureAwait(false);

        var queryResult = fetchResult
            .OnFailure(error => _logger.LogWarning(
                "Failed to query workflow {WorkflowId}: {Error}",
                request.WorkflowId,
                error.Message))
            .Map(workflow => new QueryWorkflowResponse
            {
                Result = BuildWorkflowStatePayload(workflow)
            });

        return queryResult.Match(
            response => response,
            error => throw ToRpcException(
                error,
                StatusCode.NotFound,
                $"Workflow '{request.WorkflowId}' not found"));
    }

    private static Result<Guid> ParseNamespaceId(string namespaceId) =>
        string.IsNullOrWhiteSpace(namespaceId)
            ? Result.Fail<Guid>(Error.From("Namespace ID is required", "INVALID_ARGUMENT"))
            : Guid.TryParse(namespaceId, out var parsed)
                ? Result.Ok(parsed)
                : Result.Fail<Guid>(Error.From(
                    $"Namespace ID '{namespaceId}' is not a valid GUID",
                    "INVALID_ARGUMENT"));

    private readonly record struct StartWorkflowState(
        DomainWorkflowExecution Execution,
        string WorkflowId,
        Guid RunId);

    private readonly record struct TerminateWorkflowState(
        TerminateWorkflowRequest Request,
        Guid NamespaceId,
        DomainWorkflowExecution Execution);

    private Task<Result<DomainWorkflowExecution>> FetchWorkflowAsync(
        Guid namespaceId,
        string workflowId,
        string? runId,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(runId)
            ? _workflowRepository.GetCurrentAsync(namespaceId.ToString(), workflowId, cancellationToken)
            : _workflowRepository.GetAsync(namespaceId.ToString(), workflowId, runId, cancellationToken);

    private static Struct BuildWorkflowStatePayload(DomainWorkflowExecution workflow) =>
        new()
        {
            Fields =
            {
                ["status"] = Value.ForString(workflow.WorkflowState.ToString()),
                ["workflowType"] = Value.ForString(workflow.WorkflowType),
                ["startTime"] = Value.ForString(workflow.StartedAt.UtcDateTime.ToString("O"))
            }
        };

    private static RpcException ToRpcException(
        Error error,
        StatusCode fallbackStatus,
        string fallbackMessage)
    {
        var status = error.Code switch
        {
            "INVALID_ARGUMENT" => StatusCode.InvalidArgument,
            "FAILED_PRECONDITION" => StatusCode.FailedPrecondition,
            OdinErrorCodes.WorkflowNotFound => StatusCode.NotFound,
            OdinErrorCodes.NamespaceNotFound => StatusCode.NotFound,
            OdinErrorCodes.PersistenceError => StatusCode.Internal,
            OdinErrorCodes.WorkflowExecutionFailed => StatusCode.Internal,
            _ => fallbackStatus
        };

        var message = string.IsNullOrWhiteSpace(error.Message)
            ? fallbackMessage
            : error.Message;

        return new RpcException(new Status(status, message));
    }

    private static ProtoWorkflowExecution MapToProto(DomainWorkflowExecution workflow)
    {
        var proto = new ProtoWorkflowExecution
        {
            NamespaceId = workflow.NamespaceId.ToString(),
            WorkflowId = workflow.WorkflowId,
            RunId = workflow.RunId.ToString(),
            WorkflowType = workflow.WorkflowType,
            TaskQueue = workflow.TaskQueue,
            State = MapState(workflow.WorkflowState),
            StartedAt = Timestamp.FromDateTimeOffset(workflow.StartedAt),
            LastUpdatedAt = Timestamp.FromDateTimeOffset(workflow.LastUpdatedAt),
            ShardId = workflow.ShardId,
            Version = workflow.Version
        };

        if (workflow.CompletedAt is DateTimeOffset completedAt)
        {
            proto.CompletedAt = Timestamp.FromDateTimeOffset(completedAt);
        }

        return proto;
    }

    private static WorkflowStateProto MapState(DomainWorkflowState state) =>
        state switch
        {
            DomainWorkflowState.Running => WorkflowStateProto.Running,
            DomainWorkflowState.Completed => WorkflowStateProto.Completed,
            DomainWorkflowState.Failed => WorkflowStateProto.Failed,
            DomainWorkflowState.Canceled => WorkflowStateProto.Canceled,
            DomainWorkflowState.Terminated => WorkflowStateProto.Terminated,
            DomainWorkflowState.ContinuedAsNew => WorkflowStateProto.ContinuedAsNew,
            DomainWorkflowState.TimedOut => WorkflowStateProto.TimedOut,
            _ => WorkflowStateProto.Unspecified
        };
}
