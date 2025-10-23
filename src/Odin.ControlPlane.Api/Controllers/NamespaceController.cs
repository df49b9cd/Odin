using Hugo;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Functional;
using NamespaceModel = Odin.Contracts.Namespace;

namespace Odin.ControlPlane.Api.Controllers;

/// <summary>
/// Namespace management endpoints for multi-tenant isolation.
/// </summary>
[ApiController]
[Route("api/v1/namespaces")]
[Produces("application/json")]
public sealed class NamespaceController(
    INamespaceRepository namespaceRepository,
    ILogger<NamespaceController> logger) : ControllerBase
{
    private readonly INamespaceRepository _namespaceRepository = namespaceRepository;
    private readonly ILogger<NamespaceController> _logger = logger;

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
        var createPipeline = await Go.Ok(request)
            .Ensure(static r => !string.IsNullOrWhiteSpace(r.Name),
                static _ => Error.From("Namespace name is required", "INVALID_REQUEST"))
            .Ensure(static r => r.RetentionDays is null or > 0,
                static _ => Error.From("Retention days must be positive", "INVALID_REQUEST"))
            .Map(r => new Odin.Contracts.CreateNamespaceRequest
            {
                NamespaceName = r.Name,
                Description = r.Description,
                RetentionDays = r.RetentionDays ?? 30,
                HistoryArchivalEnabled = false,
                VisibilityArchivalEnabled = false
            })
            .ThenAsync((payload, ct) => _namespaceRepository.CreateAsync(payload, ct), cancellationToken)
            .ConfigureAwait(false);

        var createResult = createPipeline
            .OnSuccess(namespaceModel => _logger.LogInformation(
                "Created namespace {Name} with ID {Id}",
                namespaceModel.NamespaceName,
                namespaceModel.NamespaceId))
            .OnFailure(error => _logger.LogError(
                "Failed to create namespace {Name}: {Error}",
                request.Name,
                error.Message));

        return createResult.Match<IActionResult>(
            model => CreatedAtAction(
                nameof(GetNamespace),
                new { id = model.NamespaceId },
                model),
            error => string.Equals(error.Code, OdinErrorCodes.NamespaceAlreadyExists, StringComparison.OrdinalIgnoreCase)
                ? Conflict(AsErrorResponse(error, OdinErrorCodes.NamespaceAlreadyExists, $"Namespace '{request.Name}' already exists"))
                : BadRequest(AsErrorResponse(error, error.Code ?? "CREATE_FAILED", error.Message ?? "Failed to create namespace")));
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
        var listPipeline = await _namespaceRepository.ListAsync(pageSize, pageToken, cancellationToken)
            .ConfigureAwait(false);

        var listResult = listPipeline
            .OnFailure(error => _logger.LogError(
                "Failed to list namespaces: {Error}",
                error.Message));

        return listResult.Match<IActionResult>(
            response => Ok(response),
            error => StatusCode(
                StatusCodes.Status500InternalServerError,
                AsErrorResponse(error, "LIST_FAILED", "Failed to list namespaces")));
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
        var getResult = await _namespaceRepository.GetByIdAsync(id, cancellationToken)
            .ConfigureAwait(false);

        return getResult.Match<IActionResult>(
            model => Ok(model),
            error => NotFound(AsErrorResponse(
                error,
                error.Code ?? OdinErrorCodes.NamespaceNotFound,
                $"Namespace '{id}' not found")));
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
        var getResult = await _namespaceRepository.GetByNameAsync(name, cancellationToken)
            .ConfigureAwait(false);

        return getResult.Match<IActionResult>(
            model => Ok(model),
            error => NotFound(AsErrorResponse(
                error,
                error.Code ?? OdinErrorCodes.NamespaceNotFound,
                $"Namespace '{name}' not found")));
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
        var namespaceResult = await Go.Ok(id)
            .ThenAsync((namespaceId, ct) => _namespaceRepository.GetByIdAsync(namespaceId, ct), cancellationToken)
            .ConfigureAwait(false);

        var archivePipeline = await namespaceResult
            .Map(model => model.NamespaceName)
            .ThenAsync((namespaceName, ct) => _namespaceRepository.ArchiveAsync(namespaceName, ct), cancellationToken)
            .ConfigureAwait(false);

        var deleteResult = archivePipeline
            .OnSuccess(_ => _logger.LogInformation("Deleted namespace {Id}", id))
            .OnFailure(error => _logger.LogError(
                "Failed to delete namespace {Id}: {Error}",
                id,
                error.Message));

        return deleteResult.Match<IActionResult>(
            _ => NoContent(),
            error => string.Equals(error.Code, OdinErrorCodes.NamespaceNotFound, StringComparison.OrdinalIgnoreCase)
                ? NotFound(AsErrorResponse(
                    error,
                    OdinErrorCodes.NamespaceNotFound,
                    $"Namespace '{id}' not found"))
                : BadRequest(AsErrorResponse(
                    error,
                    error.Code ?? "DELETE_FAILED",
                    error.Message ?? "Failed to delete namespace")));
    }

    private static ErrorResponse AsErrorResponse(
        Error error,
        string fallbackCode,
        string fallbackMessage)
    {
        return new ErrorResponse
        {
            Message = string.IsNullOrWhiteSpace(error.Message)
                ? fallbackMessage
                : error.Message,
            Code = string.IsNullOrWhiteSpace(error.Code)
                ? fallbackCode
                : error.Code
        };
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
