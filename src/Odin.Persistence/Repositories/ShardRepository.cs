using System.Data;
using Dapper;
using Hugo;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.Repositories;

/// <summary>
/// PostgreSQL/MySQL implementation of shard repository.
/// Manages distributed shard ownership coordination with leases.
/// </summary>
public sealed class ShardRepository : IShardRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<ShardRepository> _logger;

    public ShardRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<ShardRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<Result<ShardLease>> AcquireLeaseAsync(
        int shardId,
        string ownerIdentity,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<ShardLease>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var leaseExpiresAt = DateTimeOffset.UtcNow.Add(leaseDuration);

            // Try to acquire or update lease
            var sql = @"
                INSERT INTO history_shards (
                    shard_id, owner_identity, lease_expires_at,
                    acquired_at, last_heartbeat, range_start, range_end
                ) VALUES (
                    @ShardId, @OwnerIdentity, @LeaseExpiresAt,
                    now(), now(), @RangeStart, @RangeEnd
                )
                ON CONFLICT (shard_id) DO UPDATE
                SET owner_identity = @OwnerIdentity,
                    lease_expires_at = @LeaseExpiresAt,
                    last_heartbeat = now()
                WHERE history_shards.lease_expires_at IS NULL
                   OR history_shards.lease_expires_at < now()
                RETURNING shard_id, owner_identity, lease_expires_at, acquired_at, last_heartbeat, range_start, range_end";

            // Calculate shard range (for 512 shards, each covers 1/512 of hash space)
            var rangeStart = shardId * (int.MaxValue / 512);
            var rangeEnd = (shardId + 1) * (int.MaxValue / 512) - 1;

            var row = await connection.QuerySingleOrDefaultAsync<ShardLeaseRow>(sql, new
            {
                ShardId = shardId,
                OwnerIdentity = ownerIdentity,
                LeaseExpiresAt = leaseExpiresAt,
                RangeStart = rangeStart,
                RangeEnd = rangeEnd
            });

            if (row == null)
            {
                // Shard is already leased by another owner
                _logger.LogDebug(
                    "Failed to acquire lease for shard {ShardId} - already owned",
                    shardId);
                return Result.Fail<ShardLease>(
                    Error.From($"Shard {shardId} is already leased", OdinErrorCodes.ShardUnavailable));
            }

            _logger.LogInformation(
                "Acquired lease for shard {ShardId} by {OwnerIdentity} until {LeaseExpiresAt}",
                shardId, ownerIdentity, leaseExpiresAt);

            return Result.Ok(row.ToShardLease());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to acquire lease for shard {ShardId}",
                shardId);
            return Result.Fail<ShardLease>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<ShardLease>> RenewLeaseAsync(
        int shardId,
        string ownerIdentity,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<ShardLease>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var newLeaseExpiry = DateTimeOffset.UtcNow.Add(leaseDuration);

            var sql = @"
                UPDATE history_shards
                SET lease_expires_at = @NewLeaseExpiry,
                    last_heartbeat = now()
                WHERE shard_id = @ShardId
                  AND owner_identity = @OwnerIdentity
                  AND lease_expires_at > now()
                RETURNING shard_id, owner_identity, lease_expires_at, acquired_at, last_heartbeat, range_start, range_end";

            var row = await connection.QuerySingleOrDefaultAsync<ShardLeaseRow>(sql, new
            {
                ShardId = shardId,
                OwnerIdentity = ownerIdentity,
                NewLeaseExpiry = newLeaseExpiry
            });

            if (row == null)
            {
                _logger.LogWarning(
                    "Failed to renew lease for shard {ShardId} - not owned by {OwnerIdentity} or expired",
                    shardId, ownerIdentity);
                return Result.Fail<ShardLease>(
                    Error.From($"Shard {shardId} lease not found or expired", OdinErrorCodes.ShardUnavailable));
            }

            _logger.LogDebug(
                "Renewed lease for shard {ShardId} by {OwnerIdentity} until {NewLeaseExpiry}",
                shardId, ownerIdentity, newLeaseExpiry);

            return Result.Ok(row.ToShardLease());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to renew lease for shard {ShardId}",
                shardId);
            return Result.Fail<ShardLease>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<Unit>> ReleaseLeaseAsync(
        int shardId,
        string ownerIdentity,
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
                UPDATE history_shards
                SET owner_identity = NULL,
                    lease_expires_at = NULL
                WHERE shard_id = @ShardId
                  AND owner_identity = @OwnerIdentity";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                ShardId = shardId,
                OwnerIdentity = ownerIdentity
            });

            if (rowsAffected == 0)
            {
                _logger.LogWarning(
                    "Failed to release lease for shard {ShardId} - not owned by {OwnerIdentity}",
                    shardId, ownerIdentity);
                return Result.Fail<Unit>(
                    Error.From($"Shard {shardId} not owned by {ownerIdentity}", OdinErrorCodes.ShardUnavailable));
            }

            _logger.LogInformation(
                "Released lease for shard {ShardId} by {OwnerIdentity}",
                shardId, ownerIdentity);

            return Result.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to release lease for shard {ShardId}",
                shardId);
            return Result.Fail<Unit>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<ShardLease?>> GetLeaseAsync(
        int shardId,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<ShardLease?>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var sql = @"
                SELECT shard_id, owner_identity, lease_expires_at, acquired_at, last_heartbeat, range_start, range_end
                FROM history_shards
                WHERE shard_id = @ShardId
                  AND lease_expires_at IS NOT NULL
                  AND lease_expires_at > now()";

            var row = await connection.QuerySingleOrDefaultAsync<ShardLeaseRow>(sql, new
            {
                ShardId = shardId
            });

            return Result.Ok<ShardLease?>(row?.ToShardLease());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get lease for shard {ShardId}",
                shardId);
            return Result.Fail<ShardLease?>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<IReadOnlyList<int>>> GetOwnedShardsAsync(
        string ownerIdentity,
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<IReadOnlyList<int>>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var sql = @"
                SELECT shard_id
                FROM history_shards
                WHERE owner_identity = @OwnerIdentity
                  AND lease_expires_at IS NOT NULL
                  AND lease_expires_at > now()
                ORDER BY shard_id";

            var shardIds = await connection.QueryAsync<int>(sql, new
            {
                OwnerIdentity = ownerIdentity
            });

            var shardIdsList = shardIds.ToList();

            _logger.LogDebug(
                "Owner {OwnerIdentity} has {Count} active shard leases",
                ownerIdentity, shardIdsList.Count);

            return Result.Ok<IReadOnlyList<int>>(shardIdsList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get owned shards for {OwnerIdentity}",
                ownerIdentity);
            return Result.Fail<IReadOnlyList<int>>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<IReadOnlyList<ShardLease>>> ListAllShardsAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<IReadOnlyList<ShardLease>>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var sql = @"
                SELECT shard_id, owner_identity, lease_expires_at, acquired_at, last_heartbeat, range_start, range_end
                FROM history_shards
                WHERE lease_expires_at IS NOT NULL
                  AND lease_expires_at > now()
                ORDER BY shard_id";

            var rows = await connection.QueryAsync<ShardLeaseRow>(sql);
            var leases = rows.Select(r => r.ToShardLease()).ToList();

            return Result.Ok<IReadOnlyList<ShardLease>>(leases);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list all shards");
            return Result.Fail<IReadOnlyList<ShardLease>>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        var connectionResult = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
        {
            return Result.Fail<int>(connectionResult.Error!);
        }

        using var connection = connectionResult.Value;

        try
        {
            var sql = @"
                UPDATE history_shards
                SET owner_identity = NULL,
                    lease_expires_at = NULL
                WHERE lease_expires_at IS NOT NULL
                  AND lease_expires_at < now()";

            var count = await connection.ExecuteAsync(sql);

            if (count > 0)
            {
                _logger.LogInformation(
                    "Reclaimed {Count} expired shard leases",
                    count);
            }

            return Result.Ok(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reclaim expired shard leases");
            return Result.Fail<int>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }

    public async Task<Result<Unit>> InitializeShardsAsync(
        int shardCount = 512,
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
            using var transaction = connection.BeginTransaction();

            // Check if shards already exist
            var existingCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM history_shards",
                transaction: transaction);

            if (existingCount > 0)
            {
                _logger.LogInformation(
                    "Shards already initialized ({Count} shards)",
                    existingCount);
                return Result.Ok(Unit.Value);
            }

            // Initialize all shards
            var sql = @"
                INSERT INTO history_shards (
                    shard_id, range_start, range_end
                ) VALUES (
                    @ShardId, @RangeStart, @RangeEnd
                )";

            for (int i = 0; i < shardCount; i++)
            {
                var rangeStart = i * (int.MaxValue / shardCount);
                var rangeEnd = (i + 1) * (int.MaxValue / shardCount) - 1;

                await connection.ExecuteAsync(sql, new
                {
                    ShardId = i,
                    RangeStart = rangeStart,
                    RangeEnd = rangeEnd
                }, transaction);
            }

            transaction.Commit();

            _logger.LogInformation(
                "Initialized {ShardCount} shards",
                shardCount);

            return Result.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize shards");
            return Result.Fail<Unit>(
                Error.From($"Database error: {ex.Message}", OdinErrorCodes.PersistenceError));
        }
    }
}

/// <summary>
/// Internal DTO for mapping shard lease database rows.
/// </summary>
internal sealed class ShardLeaseRow
{
    public int ShardId { get; set; }
    public string? OwnerIdentity { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? AcquiredAt { get; set; }
    public DateTimeOffset? LastHeartbeat { get; set; }
    public long RangeStart { get; set; }
    public long RangeEnd { get; set; }

    public ShardLease ToShardLease()
    {
        return new ShardLease(
            ShardId: ShardId,
            OwnerHost: OwnerIdentity ?? string.Empty,
            LeaseExpiry: LeaseExpiresAt ?? DateTimeOffset.MinValue,
            HashRangeStart: RangeStart,
            HashRangeEnd: RangeEnd
        );
    }
}
