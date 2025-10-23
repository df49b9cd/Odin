using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using GrpcGetWorkflowRequest = Odin.ControlPlane.Grpc.GetWorkflowRequest;
using GrpcQueryWorkflowRequest = Odin.ControlPlane.Grpc.QueryWorkflowRequest;
using GrpcQueryWorkflowResponse = Odin.ControlPlane.Grpc.QueryWorkflowResponse;
using GrpcSignalWorkflowRequest = Odin.ControlPlane.Grpc.SignalWorkflowRequest;
using GrpcStartWorkflowRequest = Odin.ControlPlane.Grpc.StartWorkflowRequest;
using GrpcTerminateWorkflowRequest = Odin.ControlPlane.Grpc.TerminateWorkflowRequest;
using ProtoWorkflowExecution = Odin.ControlPlane.Grpc.WorkflowExecution;
using ProtoWorkflowState = Odin.ControlPlane.Grpc.WorkflowState;
using RpcStatusCode = Grpc.Core.StatusCode;
using WorkflowExecutionModel = Odin.Contracts.WorkflowExecution;
using WorkflowServiceClient = Odin.ControlPlane.Grpc.WorkflowService.WorkflowServiceClient;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Workflow lifecycle management endpoints implemented as a facade over the gRPC control plane.
/// </summary>
[ApiController]
[Route("api/v1/workflows")]
[Produces("application/json")]
public sealed class WorkflowController(
    WorkflowServiceClient workflowClient,
    ILogger<WorkflowController> logger) : ControllerBase
{
    private readonly WorkflowServiceClient _workflowClient = workflowClient;
    private readonly ILogger<WorkflowController> _logger = logger;

    /// <summary>
    /// Start a new workflow execution.
    /// </summary>
    [HttpPost("start")]
    [ProducesResponseType(typeof(StartWorkflowResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartWorkflow(
        [FromBody] StartWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var grpcRequest = new GrpcStartWorkflowRequest
        {
            NamespaceId = request.NamespaceId,
            WorkflowId = request.WorkflowId ?? string.Empty,
            WorkflowType = request.WorkflowType,
            TaskQueue = request.TaskQueue,
            Input = request.Input ?? string.Empty
        };

        try
        {
            var response = await _workflowClient
                .StartWorkflowAsync(grpcRequest, cancellationToken: cancellationToken)
                .ResponseAsync
                .ConfigureAwait(false);

            return CreatedAtAction(
                nameof(GetWorkflow),
                new { id = response.WorkflowId },
                new StartWorkflowResponse
                {
                    WorkflowId = response.WorkflowId,
                    RunId = response.RunId
                });
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Failed to start workflow {WorkflowType}", request.WorkflowType);
            return HandleRpcException(
                ex,
                "Failed to start workflow",
                "CREATE_FAILED",
                invalidArgumentCode: "INVALID_REQUEST");
        }
    }

    /// <summary>
    /// Get workflow execution details.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(WorkflowExecutionModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkflow(
        [FromRoute] string id,
        [FromQuery] string? namespaceId = null,
        [FromQuery] string? runId = null,
        CancellationToken cancellationToken = default)
    {
        var grpcRequest = new GrpcGetWorkflowRequest
        {
            NamespaceId = namespaceId ?? "default",
            WorkflowId = id,
            RunId = runId ?? string.Empty
        };

        try
        {
            var response = await _workflowClient
                .GetWorkflowAsync(grpcRequest, cancellationToken: cancellationToken)
                .ResponseAsync
                .ConfigureAwait(false);

            return Ok(MapToDomain(response.Execution));
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch workflow {WorkflowId}", id);
            return HandleRpcException(
                ex,
                "Failed to fetch workflow",
                "WORKFLOW_FETCH_FAILED",
                invalidArgumentCode: "INVALID_REQUEST",
                notFoundCode: OdinErrorCodes.WorkflowNotFound);
        }
    }

    /// <summary>
    /// Signal a running workflow.
    /// </summary>
    [HttpPost("{id}/signal")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SignalWorkflow(
        [FromRoute] string id,
        [FromBody] SignalWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var grpcRequest = new GrpcSignalWorkflowRequest
        {
            NamespaceId = request.NamespaceId ?? "default",
            WorkflowId = id,
            SignalName = request.SignalName,
            Input = request.Input ?? string.Empty
        };

        try
        {
            await _workflowClient
                .SignalWorkflowAsync(grpcRequest, cancellationToken: cancellationToken)
                .ResponseAsync
                .ConfigureAwait(false);

            _logger.LogInformation("Signal {Signal} accepted for workflow {WorkflowId}",
                request.SignalName, id);

            return Accepted();
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Failed to signal workflow {WorkflowId}", id);
            return HandleRpcException(
                ex,
                "Failed to signal workflow",
                "SIGNAL_FAILED",
                invalidArgumentCode: "INVALID_REQUEST",
                notFoundCode: OdinErrorCodes.WorkflowNotFound,
                failedPreconditionCode: "INVALID_WORKFLOW_STATE");
        }
    }

    /// <summary>
    /// Terminate a running workflow.
    /// </summary>
    [HttpPost("{id}/terminate")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TerminateWorkflow(
        [FromRoute] string id,
        [FromBody] TerminateWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var grpcRequest = new GrpcTerminateWorkflowRequest
        {
            NamespaceId = request.NamespaceId ?? "default",
            WorkflowId = id,
            Reason = request.Reason ?? "Terminated via REST facade"
        };

        try
        {
            await _workflowClient
                .TerminateWorkflowAsync(grpcRequest, cancellationToken: cancellationToken)
                .ResponseAsync
                .ConfigureAwait(false);

            _logger.LogInformation("Terminated workflow {WorkflowId}", id);
            return Accepted();
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Failed to terminate workflow {WorkflowId}", id);
            return HandleRpcException(
                ex,
                "Failed to terminate workflow",
                "TERMINATE_FAILED",
                invalidArgumentCode: "INVALID_REQUEST",
                notFoundCode: OdinErrorCodes.WorkflowNotFound,
                failedPreconditionCode: "INVALID_WORKFLOW_STATE");
        }
    }

    /// <summary>
    /// Query a workflow execution (read-only operation).
    /// </summary>
    [HttpPost("{id}/query")]
    [ProducesResponseType(typeof(QueryWorkflowResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> QueryWorkflow(
        [FromRoute] string id,
        [FromBody] QueryWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var grpcRequest = new GrpcQueryWorkflowRequest
        {
            NamespaceId = request.NamespaceId ?? "default",
            WorkflowId = id,
            QueryType = request.QueryType,
            Input = request.Args is null ? string.Empty : JsonSerializer.Serialize(request.Args)
        };

        try
        {
            GrpcQueryWorkflowResponse response = await _workflowClient
                .QueryWorkflowAsync(grpcRequest, cancellationToken: cancellationToken)
                .ResponseAsync
                .ConfigureAwait(false);

            _logger.LogInformation("Query {Query} executed for workflow {WorkflowId}",
                request.QueryType, id);

            return Ok(new QueryWorkflowResponse
            {
                Result = ConvertStruct(response.Result)
            });
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Failed to query workflow {WorkflowId}", id);
            return HandleRpcException(
                ex,
                "Failed to query workflow",
                "QUERY_FAILED",
                invalidArgumentCode: "INVALID_REQUEST",
                notFoundCode: OdinErrorCodes.WorkflowNotFound);
        }
    }

    private IActionResult HandleRpcException(
        RpcException exception,
        string defaultMessage,
        string defaultCode,
        string? invalidArgumentCode = null,
        string? notFoundCode = null,
        string? failedPreconditionCode = null)
    {
        var message = string.IsNullOrWhiteSpace(exception.Status.Detail)
            ? defaultMessage
            : exception.Status.Detail;

        switch (exception.StatusCode)
        {
            case RpcStatusCode.InvalidArgument:
                return BadRequest(new ErrorResponse
                {
                    Message = message,
                    Code = invalidArgumentCode ?? defaultCode
                });
            case RpcStatusCode.NotFound:
                return NotFound(new ErrorResponse
                {
                    Message = message,
                    Code = notFoundCode ?? defaultCode
                });
            case RpcStatusCode.FailedPrecondition:
                return BadRequest(new ErrorResponse
                {
                    Message = message,
                    Code = failedPreconditionCode ?? defaultCode
                });
            case RpcStatusCode.DeadlineExceeded:
                return CreateErrorResult(StatusCodes.Status504GatewayTimeout, message, defaultCode);
            default:
                return CreateErrorResult(StatusCodes.Status500InternalServerError, message, defaultCode);
        }
    }

    private static IActionResult CreateErrorResult(int statusCode, string message, string code) =>
        new ObjectResult(new ErrorResponse
        {
            Message = message,
            Code = code
        })
        {
            StatusCode = statusCode
        };

    private static WorkflowExecutionModel MapToDomain(ProtoWorkflowExecution execution)
    {
        var namespaceId = Guid.TryParse(execution.NamespaceId, out var parsedNamespaceId)
            ? parsedNamespaceId
            : Guid.Empty;
        var runId = Guid.TryParse(execution.RunId, out var parsedRunId)
            ? parsedRunId
            : Guid.Empty;

        return new WorkflowExecutionModel
        {
            NamespaceId = namespaceId,
            WorkflowId = execution.WorkflowId,
            RunId = runId,
            WorkflowType = execution.WorkflowType,
            TaskQueue = execution.TaskQueue,
            WorkflowState = MapState(execution.State),
            StartedAt = execution.StartedAt?.ToDateTimeOffset() ?? DateTimeOffset.MinValue,
            CompletedAt = execution.CompletedAt?.ToDateTimeOffset(),
            LastUpdatedAt = execution.LastUpdatedAt?.ToDateTimeOffset() ?? DateTimeOffset.MinValue,
            ShardId = execution.ShardId,
            Version = execution.Version
        };
    }

    private static Odin.Contracts.WorkflowState MapState(ProtoWorkflowState state) =>
        state switch
        {
            ProtoWorkflowState.Running => Odin.Contracts.WorkflowState.Running,
            ProtoWorkflowState.Completed => Odin.Contracts.WorkflowState.Completed,
            ProtoWorkflowState.Failed => Odin.Contracts.WorkflowState.Failed,
            ProtoWorkflowState.Canceled => Odin.Contracts.WorkflowState.Canceled,
            ProtoWorkflowState.Terminated => Odin.Contracts.WorkflowState.Terminated,
            ProtoWorkflowState.ContinuedAsNew => Odin.Contracts.WorkflowState.ContinuedAsNew,
            ProtoWorkflowState.TimedOut => Odin.Contracts.WorkflowState.TimedOut,
            _ => Odin.Contracts.WorkflowState.Running
        };

    private static Dictionary<string, object?> ConvertStruct(Struct? payload)
    {
        if (payload is null)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in payload.Fields)
        {
            result[key] = ConvertValue(value);
        }

        return result;
    }

    private static object? ConvertValue(Value value) =>
        value.KindCase switch
        {
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.NumberValue => value.NumberValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.StructValue => ConvertStruct(value.StructValue),
            Value.KindOneofCase.ListValue => value.ListValue.Values.Select(ConvertValue).ToList(),
            _ => null
        };
}

/// <summary>
/// Request to start a workflow.
/// </summary>
public sealed record StartWorkflowRequest
{
    public string NamespaceId { get; init; } = "default";
    public string? WorkflowId { get; init; }
    public required string WorkflowType { get; init; }
    public required string TaskQueue { get; init; }
    public string? Input { get; init; }
}

/// <summary>
/// Response from starting a workflow.
/// </summary>
public sealed record StartWorkflowResponse
{
    public required string WorkflowId { get; init; }
    public required string RunId { get; init; }
}

/// <summary>
/// Request to signal a workflow.
/// </summary>
public sealed record SignalWorkflowRequest
{
    public string? NamespaceId { get; init; }
    public required string SignalName { get; init; }
    public string? Input { get; init; }
}

/// <summary>
/// Request to terminate a workflow.
/// </summary>
public sealed record TerminateWorkflowRequest
{
    public string? NamespaceId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Request to query a workflow.
/// </summary>
public sealed record QueryWorkflowRequest
{
    public string? NamespaceId { get; init; }
    public required string QueryType { get; init; }
    public Dictionary<string, object>? Args { get; init; }
}

/// <summary>
/// Response from querying a workflow.
/// </summary>
public sealed record QueryWorkflowResponse
{
    public required Dictionary<string, object?> Result { get; init; }
}
