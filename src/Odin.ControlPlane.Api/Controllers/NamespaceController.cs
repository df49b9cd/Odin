using Hugo;
using Microsoft.AspNetCore.Mvc;
using Odin.Persistence.Interfaces;
using static Hugo.Go;
using NamespaceModel = Odin.Contracts.Namespace;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Namespace management endpoints for multi-tenant isolation.
/// </summary>
[ApiController]
[Route("api/v1/namespaces")]
[Produces("application/json")]
public sealed class NamespaceController : ControllerBase
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<NamespaceController> _logger;

    public NamespaceController(
        INamespaceRepository namespaceRepository,
        ILogger<NamespaceController> logger)
    {
        _namespaceRepository = namespaceRepository;
        _logger = logger;
    }

    /// <summary>
    /// Create a new namespace.
    /// </summary>
    /// <param name="request">Namespace creation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created namespace</returns>
    [HttpPost]
    [ProducesResponseType(typeof(NamespaceModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateNamespace(
        [FromBody] CreateNamespaceRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ErrorResponse
            {
                Message = "Namespace name is required",
                Code = "INVALID_REQUEST"
            });
        }

        var createRequest = new Odin.Contracts.CreateNamespaceRequest
        {
            NamespaceName = request.Name,
            Description = request.Description,
            RetentionDays = request.RetentionDays ?? 30,
            HistoryArchivalEnabled = false,
            VisibilityArchivalEnabled = false
        };

        var result = await _namespaceRepository.CreateAsync(createRequest, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError("Failed to create namespace {Name}: {Error}",
                request.Name, result.Error?.Message);

            // Check if duplicate
            if (result.Error?.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Conflict(new ErrorResponse
                {
                    Message = $"Namespace '{request.Name}' already exists",
                    Code = "NAMESPACE_ALREADY_EXISTS"
                });
            }

            return BadRequest(new ErrorResponse
            {
                Message = result.Error?.Message ?? "Failed to create namespace",
                Code = result.Error?.Code ?? "CREATE_FAILED"
            });
        }

        _logger.LogInformation("Created namespace {Name} with ID {Id}",
            result.Value.NamespaceName, result.Value.NamespaceId);

        return CreatedAtAction(
            nameof(GetNamespace),
            new { id = result.Value.NamespaceId },
            result.Value);
    }

    /// <summary>
    /// List all namespaces.
    /// </summary>
    /// <param name="pageSize"></param>
    /// <param name="pageToken"></param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of namespaces</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Odin.Contracts.ListNamespacesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListNamespaces(
        [FromQuery] int pageSize = 100,
        [FromQuery] string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _namespaceRepository.ListAsync(pageSize, pageToken, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogError("Failed to list namespaces: {Error}", result.Error?.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Message = "Failed to list namespaces",
                Code = "LIST_FAILED"
            });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get a specific namespace by ID.
    /// </summary>
    /// <param name="id">Namespace ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Namespace details</returns>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(NamespaceModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNamespace(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _namespaceRepository.GetByIdAsync(id, cancellationToken);

        if (result.IsFailure)
        {
            return NotFound(new ErrorResponse
            {
                Message = $"Namespace '{id}' not found",
                Code = "NAMESPACE_NOT_FOUND"
            });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Get a namespace by name.
    /// </summary>
    /// <param name="name">Namespace name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Namespace details</returns>
    [HttpGet("by-name/{name}")]
    [ProducesResponseType(typeof(NamespaceModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNamespaceByName(
        [FromRoute] string name,
        CancellationToken cancellationToken)
    {
        var result = await _namespaceRepository.GetByNameAsync(name, cancellationToken);

        if (result.IsFailure)
        {
            return NotFound(new ErrorResponse
            {
                Message = $"Namespace '{name}' not found",
                Code = "NAMESPACE_NOT_FOUND"
            });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Delete a namespace.
    /// </summary>
    /// <param name="id">Namespace ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>No content on success</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteNamespace(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        // Check if namespace exists
        var getResult = await _namespaceRepository.GetByIdAsync(id, cancellationToken);
        if (getResult.IsFailure)
        {
            return NotFound(new ErrorResponse
            {
                Message = $"Namespace '{id}' not found",
                Code = "NAMESPACE_NOT_FOUND"
            });
        }

        var deleteResult = await _namespaceRepository.ArchiveAsync(getResult.Value.NamespaceName, cancellationToken);

        if (deleteResult.IsFailure)
        {
            _logger.LogError("Failed to delete namespace {Id}: {Error}",
                id, deleteResult.Error?.Message);

            return BadRequest(new ErrorResponse
            {
                Message = deleteResult.Error?.Message ?? "Failed to delete namespace",
                Code = deleteResult.Error?.Code ?? "DELETE_FAILED"
            });
        }

        _logger.LogInformation("Deleted namespace {Id}", id);

        return NoContent();
    }
}

/// <summary>
/// Request to create a namespace (API-specific).
/// </summary>
public sealed record CreateNamespaceRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int? RetentionDays { get; init; }
}
