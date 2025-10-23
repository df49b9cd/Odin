using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Odin.Contracts;
using Odin.ControlPlane.Grpc;
using Odin.Persistence.Interfaces;
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
        if (string.IsNullOrWhiteSpace(request.WorkflowType))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "Workflow type is required"));
        }

        if (string.IsNullOrWhiteSpace(request.TaskQueue))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "Task queue is required"));
        }

        var namespaceId = ParseNamespaceId(request.NamespaceId);
        var workflowId = string.IsNullOrWhiteSpace(request.WorkflowId)
            ? Guid.NewGuid().ToString()
            : request.WorkflowId;
        var runId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var execution = new DomainWorkflowExecution
        {
            NamespaceId = namespaceId,
            WorkflowId = workflowId,
            RunId = runId,
            WorkflowType = request.WorkflowType,
            TaskQueue = request.TaskQueue,
            WorkflowState = DomainWorkflowState.Running,
            StartedAt = now,
            LastUpdatedAt = now,
            ShardId = _workflowRepository.CalculateShardId(workflowId)
        };

        var result = await _workflowRepository.CreateAsync(execution, context.CancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            _logger.LogError("Failed to start workflow {WorkflowId}: {Error}",
                workflowId,
                result.Error?.Message);

            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                result.Error?.Message ?? "Failed to start workflow"));
        }

        _logger.LogInformation(
            "Started workflow {WorkflowType} with ID {WorkflowId}/{RunId}",
            request.WorkflowType,
            workflowId,
            runId);

        return new StartWorkflowResponse
        {
            WorkflowId = workflowId,
            RunId = runId.ToString()
        };
    }

    /// <inheritdoc />
    public override async Task<GetWorkflowResponse> GetWorkflow(
        GetWorkflowRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "Workflow ID is required"));
        }

        var namespaceId = ParseNamespaceId(request.NamespaceId);
        var cancellationToken = context.CancellationToken;

        var result = string.IsNullOrWhiteSpace(request.RunId)
            ? await _workflowRepository.GetCurrentAsync(namespaceId.ToString(), request.WorkflowId, cancellationToken)
                .ConfigureAwait(false)
            : await _workflowRepository.GetAsync(namespaceId.ToString(), request.WorkflowId, request.RunId, cancellationToken)
                .ConfigureAwait(false);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                result.Error?.Message ?? $"Workflow '{request.WorkflowId}' not found"));
        }

        return new GetWorkflowResponse
        {
            Execution = MapToProto(result.Value)
        };
    }

    /// <inheritdoc />
    public override async Task<SignalWorkflowResponse> SignalWorkflow(
        SignalWorkflowRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.SignalName))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "Signal name is required"));
        }

        var namespaceId = ParseNamespaceId(request.NamespaceId);
        var cancellationToken = context.CancellationToken;

        var result = string.IsNullOrWhiteSpace(request.RunId)
            ? await _workflowRepository.GetCurrentAsync(namespaceId.ToString(), request.WorkflowId, cancellationToken)
                .ConfigureAwait(false)
            : await _workflowRepository.GetAsync(namespaceId.ToString(), request.WorkflowId, request.RunId, cancellationToken)
                .ConfigureAwait(false);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                result.Error?.Message ?? $"Workflow '{request.WorkflowId}' not found"));
        }

        if (result.Value.WorkflowState != DomainWorkflowState.Running)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"Workflow is not running (current status: {result.Value.WorkflowState})"));
        }

        _logger.LogInformation(
            "Signal {SignalName} accepted for workflow {WorkflowId}",
            request.SignalName,
            request.WorkflowId);

        return new SignalWorkflowResponse();
    }

    /// <inheritdoc />
    public override async Task<TerminateWorkflowResponse> TerminateWorkflow(
        TerminateWorkflowRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "Workflow ID is required"));
        }

        var namespaceId = ParseNamespaceId(request.NamespaceId);
        var cancellationToken = context.CancellationToken;

        var getResult = string.IsNullOrWhiteSpace(request.RunId)
            ? await _workflowRepository.GetCurrentAsync(namespaceId.ToString(), request.WorkflowId, cancellationToken)
                .ConfigureAwait(false)
            : await _workflowRepository.GetAsync(namespaceId.ToString(), request.WorkflowId, request.RunId, cancellationToken)
                .ConfigureAwait(false);

        if (getResult.IsFailure)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                getResult.Error?.Message ?? $"Workflow '{request.WorkflowId}' not found"));
        }

        if (getResult.Value.WorkflowState != DomainWorkflowState.Running)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"Workflow is not running (current status: {getResult.Value.WorkflowState})"));
        }

        var terminateResult = await _workflowRepository.TerminateAsync(
                namespaceId.ToString(),
                request.WorkflowId,
                getResult.Value.RunId.ToString(),
                string.IsNullOrWhiteSpace(request.Reason) ? "Terminated via gRPC" : request.Reason,
                cancellationToken)
            .ConfigureAwait(false);

        if (terminateResult.IsFailure)
        {
            _logger.LogError("Failed to terminate workflow {WorkflowId}: {Error}",
                request.WorkflowId,
                terminateResult.Error?.Message);

            throw new RpcException(new Status(
                StatusCode.Internal,
                terminateResult.Error?.Message ?? "Failed to terminate workflow"));
        }

        _logger.LogInformation(
            "Terminated workflow {WorkflowId}/{RunId}: {Reason}",
            request.WorkflowId,
            getResult.Value.RunId,
            request.Reason);

        return new TerminateWorkflowResponse();
    }

    /// <inheritdoc />
    public override async Task<QueryWorkflowResponse> QueryWorkflow(
        QueryWorkflowRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowId))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "Workflow ID is required"));
        }

        var namespaceId = ParseNamespaceId(request.NamespaceId);
        var cancellationToken = context.CancellationToken;

        var result = string.IsNullOrWhiteSpace(request.RunId)
            ? await _workflowRepository.GetCurrentAsync(namespaceId.ToString(), request.WorkflowId, cancellationToken)
                .ConfigureAwait(false)
            : await _workflowRepository.GetAsync(namespaceId.ToString(), request.WorkflowId, request.RunId, cancellationToken)
                .ConfigureAwait(false);

        if (result.IsFailure)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                result.Error?.Message ?? $"Workflow '{request.WorkflowId}' not found"));
        }

        var workflow = result.Value;
        var payload = new Struct
        {
            Fields =
            {
                ["status"] = Value.ForString(workflow.WorkflowState.ToString()),
                ["workflowType"] = Value.ForString(workflow.WorkflowType),
                ["startTime"] = Value.ForString(workflow.StartedAt.UtcDateTime.ToString("O"))
            }
        };

        return new QueryWorkflowResponse
        {
            Result = payload
        };
    }

    private static Guid ParseNamespaceId(string namespaceId)
    {
        if (string.IsNullOrWhiteSpace(namespaceId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Namespace ID is required"));
        }

        if (!Guid.TryParse(namespaceId, out var parsed))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"Namespace ID '{namespaceId}' is not a valid GUID"));
        }

        return parsed;
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
