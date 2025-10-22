using System.Collections.Concurrent;
using Hugo;
using Odin.Core;
using Odin.Persistence.Interfaces;
using static Hugo.Go;

namespace Odin.Persistence.InMemory;

/// <summary>
/// In-memory shard lease repository for single-process development scenarios.
/// </summary>
public sealed class InMemoryShardRepository : IShardRepository
{
    private readonly ConcurrentDictionary<int, ShardState> _shards = new();
    private int _shardCount = 512;

    public Task<Result<ShardLease>> AcquireLeaseAsync(
        int shardId,
        string ownerHost,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var shard = _shards.GetOrAdd(shardId, CreateShardState);
        var now = DateTimeOffset.UtcNow;

        lock (shard.SyncRoot)
        {
            if (shard.OwnerIdentity is null ||
                shard.LeaseExpiresAt is null ||
                shard.LeaseExpiresAt <= now ||
                string.Equals(shard.OwnerIdentity, ownerHost, StringComparison.Ordinal))
            {
                shard.OwnerIdentity = ownerHost;
                shard.AcquiredAt = now;
                shard.LastHeartbeat = now;
                shard.LeaseExpiresAt = now.Add(leaseDuration);

                return Task.FromResult(Result.Ok(ToLease(shardId, shard)));
            }

            return Task.FromResult(Result.Fail<ShardLease>(
                OdinErrors.ShardUnavailable(shardId)));
        }
    }

    public Task<Result<ShardLease>> RenewLeaseAsync(
        int shardId,
        string ownerHost,
        TimeSpan extendBy,
        CancellationToken cancellationToken = default)
    {
        if (!_shards.TryGetValue(shardId, out var shard))
        {
            return Task.FromResult(Result.Fail<ShardLease>(
                OdinErrors.ShardUnavailable(shardId)));
        }

        var now = DateTimeOffset.UtcNow;

        lock (shard.SyncRoot)
        {
            if (!string.Equals(shard.OwnerIdentity, ownerHost, StringComparison.Ordinal) ||
                shard.LeaseExpiresAt is null ||
                shard.LeaseExpiresAt <= now)
            {
                return Task.FromResult(Result.Fail<ShardLease>(
                    OdinErrors.ShardUnavailable(shardId)));
            }

            shard.LeaseExpiresAt = shard.LeaseExpiresAt.Value.Add(extendBy);
            shard.LastHeartbeat = now;

            return Task.FromResult(Result.Ok(ToLease(shardId, shard)));
        }
    }

    public Task<Result<Unit>> ReleaseLeaseAsync(
        int shardId,
        string ownerHost,
        CancellationToken cancellationToken = default)
    {
        if (!_shards.TryGetValue(shardId, out var shard))
        {
            return Task.FromResult(Result.Fail<Unit>(
                OdinErrors.ShardUnavailable(shardId)));
        }

        lock (shard.SyncRoot)
        {
            if (!string.Equals(shard.OwnerIdentity, ownerHost, StringComparison.Ordinal))
            {
                return Task.FromResult(Result.Fail<Unit>(
                    OdinErrors.ShardUnavailable(shardId)));
            }

            shard.OwnerIdentity = null;
            shard.LeaseExpiresAt = null;
            shard.LastHeartbeat = null;

            return Task.FromResult(Result.Ok(Unit.Value));
        }
    }

    public Task<Result<ShardLease?>> GetLeaseAsync(
        int shardId,
        CancellationToken cancellationToken = default)
    {
        if (!_shards.TryGetValue(shardId, out var shard))
        {
            return Task.FromResult(Result.Ok<ShardLease?>(null));
        }

        var now = DateTimeOffset.UtcNow;

        lock (shard.SyncRoot)
        {
            if (shard.OwnerIdentity is null ||
                shard.LeaseExpiresAt is null ||
                shard.LeaseExpiresAt <= now)
            {
                return Task.FromResult(Result.Ok<ShardLease?>(null));
            }

            return Task.FromResult(Result.Ok<ShardLease?>(ToLease(shardId, shard)));
        }
    }

    public Task<Result<IReadOnlyList<int>>> GetOwnedShardsAsync(
        string ownerHost,
        CancellationToken cancellationToken = default)
    {
        var owned = _shards
            .Where(kvp =>
            {
                var shard = kvp.Value;
                lock (shard.SyncRoot)
                {
                    return string.Equals(shard.OwnerIdentity, ownerHost, StringComparison.Ordinal) &&
                           shard.LeaseExpiresAt is { } leaseExpiry &&
                           leaseExpiry > DateTimeOffset.UtcNow;
                }
            })
            .Select(kvp => kvp.Key)
            .OrderBy(id => id)
            .ToList();

        return Task.FromResult(Result.Ok<IReadOnlyList<int>>(owned));
    }

    public Task<Result<IReadOnlyList<ShardLease>>> ListAllShardsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var leases = new List<ShardLease>();

        foreach (var (shardId, shard) in _shards)
        {
            lock (shard.SyncRoot)
            {
                if (shard.OwnerIdentity is null ||
                    shard.LeaseExpiresAt is null ||
                    shard.LeaseExpiresAt <= now)
                {
                    continue;
                }

                leases.Add(ToLease(shardId, shard));
            }
        }

        return Task.FromResult(Result.Ok<IReadOnlyList<ShardLease>>(leases));
    }

    public Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        var reclaimed = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var shard in _shards.Values)
        {
            lock (shard.SyncRoot)
            {
                if (shard.OwnerIdentity is null ||
                    shard.LeaseExpiresAt is null ||
                    shard.LeaseExpiresAt > now)
                {
                    continue;
                }

                shard.OwnerIdentity = null;
                shard.LeaseExpiresAt = null;
                shard.LastHeartbeat = null;
                reclaimed++;
            }
        }

        return Task.FromResult(Result.Ok(reclaimed));
    }

    public Task<Result<Unit>> InitializeShardsAsync(
        int shardCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shardCount, 1);

        _shardCount = Math.Max(1, shardCount);

        for (var i = 0; i < shardCount; i++)
        {
            _shards.GetOrAdd(i, static (id, state) => CreateShardState(id, state.ShardCount), new State(_shardCount));
        }

        return Task.FromResult(Result.Ok(Unit.Value));
    }

    private ShardState CreateShardState(int shardId)
        => CreateShardState(shardId, _shardCount);

    private static ShardState CreateShardState(int shardId, int shardCount)
    {
        // Mirror database implementation where shards evenly split the hash range.
        var denominator = Math.Max(1, shardCount);
        var partitionSize = (int.MaxValue / denominator) == 0 ? 1 : int.MaxValue / denominator;
        var rangeStart = shardId * partitionSize;
        var rangeEnd = ((shardId + 1) * partitionSize) - 1;

        return new ShardState
        {
            RangeStart = rangeStart,
            RangeEnd = rangeEnd
        };
    }

    private sealed record State(int ShardCount);

    private static ShardLease ToLease(int shardId, ShardState shard) => new(
        ShardId: shardId,
        OwnerHost: shard.OwnerIdentity ?? string.Empty,
        LeaseExpiry: shard.LeaseExpiresAt ?? DateTimeOffset.MinValue,
        HashRangeStart: shard.RangeStart,
        HashRangeEnd: shard.RangeEnd);

    private sealed class ShardState
    {
        public string? OwnerIdentity { get; set; }
        public DateTimeOffset? LeaseExpiresAt { get; set; }
        public DateTimeOffset? AcquiredAt { get; set; }
        public DateTimeOffset? LastHeartbeat { get; set; }
        public long RangeStart { get; init; }
        public long RangeEnd { get; init; }
        public object SyncRoot { get; } = new();
    }
}
