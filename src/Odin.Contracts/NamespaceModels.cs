using System.Text.Json;

namespace Odin.Contracts;

/// <summary>
/// Represents a namespace in the Odin orchestrator
/// </summary>
public sealed record Namespace
{
    /// <summary>
    /// Unique namespace identifier
    /// </summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>
    /// Namespace name (unique across the cluster)
    /// </summary>
    public required string NamespaceName { get; init; }

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Owner identifier
    /// </summary>
    public string? OwnerId { get; init; }

    /// <summary>
    /// Retention period in days for workflow history
    /// </summary>
    public int RetentionDays { get; init; } = 30;

    /// <summary>
    /// Whether history archival is enabled
    /// </summary>
    public bool HistoryArchivalEnabled { get; init; }

    /// <summary>
    /// Whether visibility archival is enabled
    /// </summary>
    public bool VisibilityArchivalEnabled { get; init; }

    /// <summary>
    /// Whether this is a global namespace
    /// </summary>
    public bool IsGlobalNamespace { get; init; }

    /// <summary>
    /// Cluster-specific configuration
    /// </summary>
    public JsonDocument? ClusterConfig { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public JsonDocument? Data { get; init; }

    /// <summary>
    /// Timestamp when the namespace was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when the namespace was last updated
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Namespace status
    /// </summary>
    public NamespaceStatus Status { get; init; } = NamespaceStatus.Active;
}

/// <summary>
/// Namespace status enumeration
/// </summary>
public enum NamespaceStatus
{
    Active,
    Deprecated,
    Deleted
}

/// <summary>
/// Request to create a new namespace
/// </summary>
public sealed record CreateNamespaceRequest
{
    /// <summary>
    /// Namespace name
    /// </summary>
    public required string NamespaceName { get; init; }

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Owner identifier
    /// </summary>
    public string? OwnerId { get; init; }

    /// <summary>
    /// Retention days (default: 30)
    /// </summary>
    public int RetentionDays { get; init; } = 30;

    /// <summary>
    /// Enable history archival
    /// </summary>
    public bool HistoryArchivalEnabled { get; init; }

    /// <summary>
    /// Enable visibility archival
    /// </summary>
    public bool VisibilityArchivalEnabled { get; init; }
}

/// <summary>
/// Request to update an existing namespace
/// </summary>
public sealed record UpdateNamespaceRequest
{
    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Retention days
    /// </summary>
    public int? RetentionDays { get; init; }

    /// <summary>
    /// Enable/disable history archival
    /// </summary>
    public bool? HistoryArchivalEnabled { get; init; }

    /// <summary>
    /// Enable/disable visibility archival
    /// </summary>
    public bool? VisibilityArchivalEnabled { get; init; }
}

/// <summary>
/// Response for namespace operations
/// </summary>
public sealed record NamespaceResponse
{
    /// <summary>
    /// The namespace
    /// </summary>
    public required Namespace Namespace { get; init; }
}

/// <summary>
/// List namespaces response
/// </summary>
public sealed record ListNamespacesResponse
{
    /// <summary>
    /// List of namespaces
    /// </summary>
    public required IReadOnlyList<Namespace> Namespaces { get; init; }

    /// <summary>
    /// Pagination token for next page
    /// </summary>
    public string? NextPageToken { get; init; }
}
