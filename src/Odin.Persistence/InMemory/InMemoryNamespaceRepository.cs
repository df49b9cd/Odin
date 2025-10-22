using System.Collections.Concurrent;
using System.Globalization;
using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.InMemory;

/// <summary>
/// In-memory implementation of <see cref="INamespaceRepository"/> used for development and prototyping.
/// </summary>
public sealed class InMemoryNamespaceRepository : INamespaceRepository
{
    private readonly ConcurrentDictionary<string, Namespace> _namespacesByName;
    private readonly ConcurrentDictionary<Guid, string> _nameIndex;
    private readonly ILogger<InMemoryNamespaceRepository> _logger;
    private readonly object _sync = new();

    public InMemoryNamespaceRepository(ILogger<InMemoryNamespaceRepository> logger)
    {
        _logger = logger;
        _namespacesByName = new ConcurrentDictionary<string, Namespace>(StringComparer.OrdinalIgnoreCase);
        _nameIndex = new ConcurrentDictionary<Guid, string>();
    }

    public Task<Result<Namespace>> CreateAsync(
        CreateNamespaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_sync)
        {
            if (_namespacesByName.TryGetValue(request.NamespaceName, out var existing) &&
                existing.Status != NamespaceStatus.Deleted)
            {
                _logger.LogWarning("Namespace {Namespace} already exists", request.NamespaceName);
                return Task.FromResult(Result.Fail<Namespace>(
                    OdinErrors.NamespaceAlreadyExists(request.NamespaceName)));
            }

            var now = DateTimeOffset.UtcNow;

            var namespaceId = Guid.NewGuid();
            var ns = new Namespace
            {
                NamespaceId = namespaceId,
                NamespaceName = request.NamespaceName,
                Description = request.Description,
                OwnerId = request.OwnerId,
                RetentionDays = request.RetentionDays,
                HistoryArchivalEnabled = request.HistoryArchivalEnabled,
                VisibilityArchivalEnabled = request.VisibilityArchivalEnabled,
                CreatedAt = now,
                UpdatedAt = now,
                Status = NamespaceStatus.Active
            };

            _namespacesByName[ns.NamespaceName] = ns;
            _nameIndex[namespaceId] = ns.NamespaceName;

            _logger.LogInformation("Created in-memory namespace {Namespace} ({NamespaceId})", ns.NamespaceName, ns.NamespaceId);

            return Task.FromResult(Result.Ok(ns));
        }
    }

    public Task<Result<Namespace>> GetByNameAsync(
        string namespaceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        if (_namespacesByName.TryGetValue(namespaceName, out var ns) &&
            ns.Status != NamespaceStatus.Deleted)
        {
            return Task.FromResult(Result.Ok(ns));
        }

        return Task.FromResult(Result.Fail<Namespace>(
            OdinErrors.NamespaceNotFound(namespaceName)));
    }

    public Task<Result<Namespace>> GetByIdAsync(
        Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        if (_nameIndex.TryGetValue(namespaceId, out var name) &&
            _namespacesByName.TryGetValue(name, out var ns) &&
            ns.Status != NamespaceStatus.Deleted)
        {
            return Task.FromResult(Result.Ok(ns));
        }

        return Task.FromResult(Result.Fail<Namespace>(
            OdinErrors.NamespaceNotFound(namespaceId.ToString())));
    }

    public Task<Result<Namespace>> UpdateAsync(
        string namespaceName,
        UpdateNamespaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);
        ArgumentNullException.ThrowIfNull(request);

        lock (_sync)
        {
            if (!_namespacesByName.TryGetValue(namespaceName, out var existing) ||
                existing.Status == NamespaceStatus.Deleted)
            {
                return Task.FromResult(Result.Fail<Namespace>(
                    OdinErrors.NamespaceNotFound(namespaceName)));
            }

            var updated = existing with
            {
                Description = request.Description ?? existing.Description,
                RetentionDays = request.RetentionDays ?? existing.RetentionDays,
                HistoryArchivalEnabled = request.HistoryArchivalEnabled ?? existing.HistoryArchivalEnabled,
                VisibilityArchivalEnabled = request.VisibilityArchivalEnabled ?? existing.VisibilityArchivalEnabled,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _namespacesByName[namespaceName] = updated;

            return Task.FromResult(Result.Ok(updated));
        }
    }

    public Task<Result<ListNamespacesResponse>> ListAsync(
        int pageSize = 100,
        string? pageToken = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPageSize = pageSize <= 0 ? 100 : pageSize;
        var startIndex = 0;

        if (!string.IsNullOrWhiteSpace(pageToken) &&
            int.TryParse(pageToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedToken) &&
            parsedToken >= 0)
        {
            startIndex = parsedToken;
        }

        var activeNamespaces = _namespacesByName.Values
            .Where(ns => ns.Status != NamespaceStatus.Deleted)
            .OrderBy(ns => ns.NamespaceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var page = activeNamespaces
            .Skip(startIndex)
            .Take(normalizedPageSize)
            .ToList();

        var nextToken = startIndex + page.Count < activeNamespaces.Count
            ? (startIndex + page.Count).ToString(CultureInfo.InvariantCulture)
            : null;

        var response = new ListNamespacesResponse
        {
            Namespaces = page,
            NextPageToken = nextToken
        };

        return Task.FromResult(Result.Ok(response));
    }

    public Task<Result<bool>> ExistsAsync(
        string namespaceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        var exists = _namespacesByName.TryGetValue(namespaceName, out var ns) &&
                     ns.Status != NamespaceStatus.Deleted;

        return Task.FromResult(Result.Ok(exists));
    }

    public Task<Result<Unit>> ArchiveAsync(
        string namespaceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceName);

        lock (_sync)
        {
            if (!_namespacesByName.TryGetValue(namespaceName, out var existing) ||
                existing.Status == NamespaceStatus.Deleted)
            {
                return Task.FromResult(Result.Fail<Unit>(
                    OdinErrors.NamespaceNotFound(namespaceName)));
            }

            var archived = existing with
            {
                Status = NamespaceStatus.Deleted,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _namespacesByName[namespaceName] = archived;

            return Task.FromResult(Result.Ok(Unit.Value));
        }
    }
}
