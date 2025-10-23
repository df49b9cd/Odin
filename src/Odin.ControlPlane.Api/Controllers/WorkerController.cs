using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Persistence.Interfaces;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Worker host visibility endpoints for the control plane UI.
/// </summary>
[ApiController]
[Route("api/v1/workers")]
[Produces("application/json")]
public sealed class WorkerController(
    IShardRepository shardRepository,
    ILogger<WorkerController> logger) : ControllerBase
{
    private readonly IShardRepository _shardRepository = shardRepository;
    private readonly ILogger<WorkerController> _logger = logger;

    /// <summary>
    /// Lists worker hosts that currently hold shard leases.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WorkerHostSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListWorkers(CancellationToken cancellationToken = default)
    {
        var shardResult = await _shardRepository
            .ListAllShardsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (shardResult.IsFailure)
        {
            var error = shardResult.Error!;
            var message = string.IsNullOrWhiteSpace(error.Message)
                ? "Failed to enumerate shard leases."
                : error.Message;
            var code = string.IsNullOrWhiteSpace(error.Code)
                ? OdinErrorCodes.PersistenceError
                : error.Code;

            _logger.LogError("{Message}: {Error}", message, error.Message);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ErrorResponse
                {
                    Message = message,
                    Code = code
                });
        }

        var summaries = shardResult.Value
            .Where(lease => !string.IsNullOrWhiteSpace(lease.OwnerHost))
            .GroupBy(lease => lease.OwnerHost, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WorkerHostSummary
            {
                HostIdentity = group.Key,
                OwnedShards = group.Count(),
                ShardIds = group.Select(lease => lease.ShardId).OrderBy(id => id).ToArray(),
                EarliestLeaseExpiry = group.Min(lease => lease.LeaseExpiry),
                LatestLeaseExpiry = group.Max(lease => lease.LeaseExpiry)
            })
            .OrderByDescending(summary => summary.OwnedShards)
            .ThenBy(summary => summary.HostIdentity, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(summaries);
    }
}

/// <summary>
/// Worker host shard ownership summary.
/// </summary>
public sealed record WorkerHostSummary
{
    public required string HostIdentity { get; init; }
    public required int OwnedShards { get; init; }
    public required IReadOnlyList<int> ShardIds { get; init; }
    public DateTimeOffset? EarliestLeaseExpiry { get; init; }
    public DateTimeOffset? LatestLeaseExpiry { get; init; }
}
