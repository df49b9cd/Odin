using Hugo;
using Odin.Contracts;
using static Hugo.Go;

namespace Odin.Persistence.Interfaces;

/// <summary>
/// Repository for managing namespace persistence operations.
/// Namespaces provide multi-tenant isolation and configuration.
/// </summary>
public interface INamespaceRepository
{
    /// <summary>
    /// Creates a new namespace with specified configuration.
    /// </summary>
    Task<Result<Namespace>> CreateAsync(
        CreateNamespaceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a namespace by name.
    /// </summary>
    Task<Result<Namespace>> GetByNameAsync(
        string namespaceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a namespace by ID.
    /// </summary>
    Task<Result<Namespace>> GetByIdAsync(
        Guid namespaceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing namespace configuration.
    /// </summary>
    Task<Result<Namespace>> UpdateAsync(
        string namespaceName,
        UpdateNamespaceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all namespaces with optional pagination.
    /// </summary>
    Task<Result<ListNamespacesResponse>> ListAsync(
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a namespace exists by name.
    /// </summary>
    Task<Result<bool>> ExistsAsync(
        string namespaceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives a namespace (soft delete).
    /// </summary>
    Task<Result<Unit>> ArchiveAsync(
        string namespaceName,
        CancellationToken cancellationToken = default);
}
