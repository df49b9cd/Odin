using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hugo;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Persistence.Interfaces;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Aggregated control plane overview endpoints for UI consumption.
/// </summary>
[ApiController]
[Route("api/v1/overview")]
[Produces("application/json")]
public sealed class OverviewController(
    INamespaceRepository namespaceRepository,
    IWorkflowExecutionRepository workflowRepository,
    ITaskQueueRepository taskQueueRepository,
    IShardRepository shardRepository,
    ILogger<OverviewController> logger) : ControllerBase
{
    private const int BatchPageSize = 500;

    private readonly INamespaceRepository _namespaceRepository = namespaceRepository;
    private readonly IWorkflowExecutionRepository _workflowRepository = workflowRepository;
    private readonly ITaskQueueRepository _taskQueueRepository = taskQueueRepository;
    private readonly IShardRepository _shardRepository = shardRepository;
    private readonly ILogger<OverviewController> _logger = logger;

    /// <summary>
    /// Returns high-level system counters used on the control plane dashboard.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SystemOverviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetOverview(CancellationToken cancellationToken = default)
    {
        var namespacesResult = await FetchAllNamespacesAsync(cancellationToken).ConfigureAwait(false);
        if (namespacesResult.IsFailure)
        {
            return HandleFailure(namespacesResult.Error!, "Failed to retrieve namespaces for overview");
        }

        var namespaceIds = namespacesResult.Value.Select(ns => ns.NamespaceId).ToList();

        var workflowCountResult = await CountWorkflowsAsync(namespaceIds, cancellationToken).ConfigureAwait(false);
        if (workflowCountResult.IsFailure)
        {
            return HandleFailure(workflowCountResult.Error!, "Failed to aggregate workflow totals");
        }

        var queueResult = await _taskQueueRepository.ListQueuesAsync(null, cancellationToken).ConfigureAwait(false);
        if (queueResult.IsFailure)
        {
            return HandleFailure(queueResult.Error!, "Failed to list task queues for overview");
        }

        var shardResult = await _shardRepository.ListAllShardsAsync(cancellationToken).ConfigureAwait(false);
        if (shardResult.IsFailure)
        {
            return HandleFailure(shardResult.Error!, "Failed to list shard leases for overview");
        }

        var activeWorkerHosts = shardResult.Value
            .Where(lease => !string.IsNullOrWhiteSpace(lease.OwnerHost))
            .Select(lease => lease.OwnerHost)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var response = new SystemOverviewResponse
        {
            Namespaces = namespaceIds.Count,
            Workflows = workflowCountResult.Value,
            ActiveTaskQueues = queueResult.Value.Count,
            ActiveWorkerHosts = activeWorkerHosts,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        return Ok(response);
    }

    private async Task<Result<IReadOnlyList<Odin.Contracts.Namespace>>> FetchAllNamespacesAsync(
        CancellationToken cancellationToken)
    {
        var collected = new List<Odin.Contracts.Namespace>();
        string? token = null;

        do
        {
            var pageResult = await _namespaceRepository
                .ListAsync(BatchPageSize, token, cancellationToken)
                .ConfigureAwait(false);

            if (pageResult.IsFailure)
            {
                return Result.Fail<IReadOnlyList<Odin.Contracts.Namespace>>(pageResult.Error!);
            }

            collected.AddRange(pageResult.Value.Namespaces);
            token = pageResult.Value.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(token));

        return Result.Ok<IReadOnlyList<Odin.Contracts.Namespace>>(collected);
    }

    private async Task<Result<int>> CountWorkflowsAsync(
        IEnumerable<Guid> namespaceIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var total = 0;

            foreach (var namespaceId in namespaceIds)
            {
                var pageToken = default(string?);
                var offset = 0;

                do
                {
                    var pageResult = await _workflowRepository
                        .ListAsync(
                            namespaceId.ToString(),
                            state: null,
                            pageSize: BatchPageSize,
                            pageToken,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (pageResult.IsFailure)
                    {
                        return Result.Fail<int>(pageResult.Error!);
                    }

                    var page = pageResult.Value;
                    total += page.Count;

                    if (page.Count == BatchPageSize)
                    {
                        offset += page.Count;
                        pageToken = offset.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        pageToken = null;
                    }
                }
                while (!string.IsNullOrWhiteSpace(pageToken));
            }

            return Result.Ok(total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while counting workflows for overview");
            return Result.Fail<int>(Error.From(ex.Message, OdinErrorCodes.PersistenceError));
        }
    }

    private IActionResult HandleFailure(Error error, string logMessage)
    {
        var message = string.IsNullOrWhiteSpace(error.Message)
            ? logMessage
            : error.Message;
        var code = string.IsNullOrWhiteSpace(error.Code)
            ? "OVERVIEW_FAILED"
            : error.Code;

        _logger.LogError("{Message}: {Error}", logMessage, message);

        return StatusCode(
            StatusCodes.Status500InternalServerError,
            new ErrorResponse
            {
                Message = message,
                Code = code
            });
    }
}

/// <summary>
/// Response payload for the system overview endpoint.
/// </summary>
public sealed record SystemOverviewResponse
{
    public required int Namespaces { get; init; }
    public required int Workflows { get; init; }
    public required int ActiveTaskQueues { get; init; }
    public required int ActiveWorkerHosts { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
}
