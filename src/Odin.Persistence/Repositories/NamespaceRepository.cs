using System;
using System.Data;
using System.Text.Json;
using Dapper;
using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;
using static Hugo.Functional;

namespace Odin.Persistence.Repositories;

/// <summary>
/// PostgreSQL/MySQL implementation of namespace repository using Dapper.
/// Provides CRUD operations for namespace management with retry logic.
/// </summary>
public sealed class NamespaceRepository(
    IDbConnectionFactory connectionFactory,
    ILogger<NamespaceRepository> logger) : INamespaceRepository
{
    private readonly IDbConnectionFactory _connectionFactory = connectionFactory;
    private readonly ILogger<NamespaceRepository> _logger = logger;

    public async Task<Result<Namespace>> CreateAsync(
        CreateNamespaceRequest request,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connectionResult
            .TapError(error => _logger.LogError(
                "Failed to open connection when creating namespace {NamespaceName}: {Error}",
                request.NamespaceName,
                error.Message))
            .ThenAsync(async (connection, ct) =>
            {
                using var dbConnection = connection;
                var sql = @"
                INSERT INTO namespaces (
                    namespace_id, namespace_name, description, owner_id,
                    retention_days, history_archival_enabled, visibility_archival_enabled,
                    status, created_at, updated_at
                ) VALUES (
                    gen_random_uuid(), @NamespaceName, @Description, @OwnerId,
                    @RetentionDays, @HistoryArchivalEnabled, @VisibilityArchivalEnabled,
                    'active', now(), now()
                )
                RETURNING namespace_id, namespace_name, description, owner_id,
                          retention_days, history_archival_enabled, visibility_archival_enabled,
                          cluster_config, is_global_namespace, data, status::text, created_at, updated_at";

                try
                {
                    var row = await dbConnection.QuerySingleAsync<NamespaceRow>(sql, new
                    {
                        request.NamespaceName,
                        request.Description,
                        request.OwnerId,
                        request.RetentionDays,
                        request.HistoryArchivalEnabled,
                        request.VisibilityArchivalEnabled
                    });

                    _logger.LogInformation(
                        "Created namespace {NamespaceName} with ID {NamespaceId}",
                        request.NamespaceName,
                        row.NamespaceId);

                    return Result.Ok(row.ToNamespace());
                }
                catch (Exception ex) when (ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Namespace {NamespaceName} already exists",
                        request.NamespaceName);
                    return Result.Fail<Namespace>(
                        OdinErrors.NamespaceAlreadyExists(request.NamespaceName));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create namespace {NamespaceName}", request.NamespaceName);
                    return Result.Fail<Namespace>(
                        Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
                }
            }, cancellationToken);
    }

    public async Task<Result<Namespace>> GetByNameAsync(
        string namespaceName,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connectionResult
            .TapError(error => _logger.LogError(
                "Failed to open connection when fetching namespace {NamespaceName}: {Error}",
                namespaceName,
                error.Message))
            .ThenAsync(async (connection, ct) =>
            {
                using var dbConnection = connection;
                var sql = @"
                SELECT namespace_id, namespace_name, description, owner_id,
                       retention_days, history_archival_enabled, visibility_archival_enabled,
                       cluster_config, is_global_namespace, data, status::text, created_at, updated_at
                FROM namespaces
                WHERE namespace_name = @Name AND status != 'deleted'";

                try
                {
                    var row = await dbConnection.QuerySingleOrDefaultAsync<NamespaceRow>(
                        sql,
                        new { Name = namespaceName });

                    if (row is null)
                    {
                        _logger.LogWarning("Namespace {NamespaceName} not found", namespaceName);
                        return Result.Fail<Namespace>(
                            OdinErrors.NamespaceNotFound(namespaceName));
                    }

                    return Result.Ok(row.ToNamespace());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get namespace {NamespaceName}", namespaceName);
                    return Result.Fail<Namespace>(
                        Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
                }
            }, cancellationToken);
    }

    public async Task<Result<Namespace>> GetByIdAsync(
        Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Namespace>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var sql = @"
                SELECT namespace_id, namespace_name, description, owner_id,
                       retention_days, history_archival_enabled, visibility_archival_enabled,
                       cluster_config, is_global_namespace, data, status::text, created_at, updated_at
                FROM namespaces
                WHERE namespace_id = @Id AND status != 'deleted'";

            var result = await connection.QuerySingleOrDefaultAsync<NamespaceRow>(
                sql, new { Id = namespaceId });

            if (result == null)
            {
                _logger.LogWarning("Namespace with ID {NamespaceId} not found", namespaceId);
                return Result.Fail<Namespace>(
                    OdinErrors.NamespaceNotFound(namespaceId.ToString()));
            }

            return Result.Ok(result.ToNamespace());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get namespace by ID {NamespaceId}", namespaceId);
            return Result.Fail<Namespace>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<Namespace>> UpdateAsync(
        string namespaceName,
        UpdateNamespaceRequest request,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Namespace>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var sql = @"
                UPDATE namespaces
                SET description = COALESCE(@Description, description),
                    retention_days = COALESCE(@RetentionDays, retention_days),
                    history_archival_enabled = COALESCE(@HistoryArchivalEnabled, history_archival_enabled),
                    visibility_archival_enabled = COALESCE(@VisibilityArchivalEnabled, visibility_archival_enabled),
                    updated_at = now()
                WHERE namespace_name = @Name AND status != 'deleted'
                RETURNING namespace_id, namespace_name, description, owner_id,
                          retention_days, history_archival_enabled, visibility_archival_enabled,
                          cluster_config, is_global_namespace, data, status::text, created_at, updated_at";

            var result = await connection.QuerySingleOrDefaultAsync<NamespaceRow>(sql, new
            {
                Name = namespaceName,
                request.Description,
                request.RetentionDays,
                request.HistoryArchivalEnabled,
                request.VisibilityArchivalEnabled
            });

            if (result == null)
            {
                _logger.LogWarning("Namespace {NamespaceName} not found for update", namespaceName);
                return Result.Fail<Namespace>(
                    OdinErrors.NamespaceNotFound(namespaceName));
            }

            _logger.LogInformation("Updated namespace {NamespaceName}", namespaceName);

            return Result.Ok(result.ToNamespace());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update namespace {NamespaceName}", namespaceName);
            return Result.Fail<Namespace>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<ListNamespacesResponse>> ListAsync(
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<ListNamespacesResponse>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var offset = string.IsNullOrEmpty(pageToken) ? 0 : int.Parse(pageToken);

            var sql = @"
                SELECT namespace_id, namespace_name, description, owner_id,
                       retention_days, history_archival_enabled, visibility_archival_enabled,
                       cluster_config, is_global_namespace, data, status::text, created_at, updated_at
                FROM namespaces
                WHERE status != 'deleted'
                ORDER BY namespace_name
                LIMIT @PageSize OFFSET @Offset";

            var rows = await connection.QueryAsync<NamespaceRow>(
                sql, new { PageSize = pageSize + 1, Offset = offset });

            var namespaces = rows.Take(pageSize).Select(r => r.ToNamespace()).ToList();
            var hasMore = rows.Count() > pageSize;
            var nextPageToken = hasMore ? (offset + pageSize).ToString() : null;

            return Result.Ok(new ListNamespacesResponse
            {
                Namespaces = namespaces,
                NextPageToken = nextPageToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list namespaces");
            return Result.Fail<ListNamespacesResponse>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<bool>> ExistsAsync(
        string namespaceName,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<bool>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var sql = @"
                SELECT EXISTS(
                    SELECT 1 FROM namespaces
                    WHERE namespace_name = @Name AND status != 'deleted'
                )";

            var exists = await connection.ExecuteScalarAsync<bool>(sql, new { Name = namespaceName });
            return Result.Ok(exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check namespace existence {NamespaceName}", namespaceName);
            return Result.Fail<bool>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<Unit>> ArchiveAsync(
        string namespaceName,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<Unit>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var sql = @"
                UPDATE namespaces
                SET status = 'deleted', updated_at = now()
                WHERE namespace_name = @Name AND status != 'deleted'";

            var rowsAffected = await connection.ExecuteAsync(sql, new { Name = namespaceName });

            if (rowsAffected == 0)
            {
                _logger.LogWarning("Namespace {NamespaceName} not found for archival", namespaceName);
                return Result.Fail<Unit>(
                    OdinErrors.NamespaceNotFound(namespaceName));
            }

            _logger.LogInformation("Archived namespace {NamespaceName}", namespaceName);

            return Result.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive namespace {NamespaceName}", namespaceName);
            return Result.Fail<Unit>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }
}

/// <summary>
/// Internal DTO for mapping database rows to Namespace models.
/// </summary>
internal sealed class NamespaceRow
{
    public Guid NamespaceId { get; set; }
    public string NamespaceName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? OwnerId { get; set; }
    public int RetentionDays { get; set; }
    public bool HistoryArchivalEnabled { get; set; }
    public bool VisibilityArchivalEnabled { get; set; }
    public string? ClusterConfig { get; set; }
    public bool IsGlobalNamespace { get; set; }
    public string? Data { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Namespace ToNamespace()
    {
        JsonDocument? clusterConfig = null;
        if (!string.IsNullOrEmpty(ClusterConfig))
        {
            clusterConfig = JsonDocument.Parse(ClusterConfig);
        }

        JsonDocument? data = null;
        if (!string.IsNullOrEmpty(Data))
        {
            data = JsonDocument.Parse(Data);
        }

        return new Namespace
        {
            NamespaceId = NamespaceId,
            NamespaceName = NamespaceName,
            Description = Description,
            OwnerId = OwnerId,
            RetentionDays = RetentionDays,
            HistoryArchivalEnabled = HistoryArchivalEnabled,
            VisibilityArchivalEnabled = VisibilityArchivalEnabled,
            IsGlobalNamespace = IsGlobalNamespace,
            ClusterConfig = clusterConfig,
            Data = data,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Status = Enum.Parse<NamespaceStatus>(Status, ignoreCase: true)
        };
    }
}
