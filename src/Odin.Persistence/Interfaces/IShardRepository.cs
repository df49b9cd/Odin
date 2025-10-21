using Hugo;
using static Hugo.Go;

namespace Odin.Persistence.Interfaces;

/// <summary>
/// Repository for managing history shard ownership and leases.
/// Coordinates distributed ownership of workflow execution shards.
/// </summary>
public interface IShardRepository
{
    /// <summary>
    /// Acquires ownership lease for a shard.
    /// Returns success if lease acquired or already owned by this host.
    /// </summary>
    Task<Result<ShardLease>> AcquireLeaseAsync(
        int shardId,
        string ownerHost,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews an existing shard lease.
    /// </summary>
    Task<Result<ShardLease>> RenewLeaseAsync(
        int shardId,
        string ownerHost,
        TimeSpan extendBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases ownership of a shard.
    /// </summary>
    Task<Result<Unit>> ReleaseLeaseAsync(
        int shardId,
        string ownerHost,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current owner of a shard.
    /// Returns null if shard is unowned.
    /// </summary>
    Task<Result<ShardLease?>> GetLeaseAsync(
        int shardId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all shards owned by a specific host.
    /// </summary>
    Task<Result<IReadOnlyList<int>>> GetOwnedShardsAsync(
        string ownerHost,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all shards with their ownership status.
    /// </summary>
    Task<Result<IReadOnlyList<ShardLease>>> ListAllShardsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reclaims expired shard leases.
    /// Should be called periodically by maintenance workers.
    /// </summary>
    Task<Result<int>> ReclaimExpiredLeasesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes shard records if they don't exist.
    /// Called during deployment initialization.
    /// </summary>
    Task<Result<Unit>> InitializeShardsAsync(
        int shardCount,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a shard ownership lease.
/// </summary>
public sealed record ShardLease(
    int ShardId,
    string OwnerHost,
    DateTimeOffset LeaseExpiry,
    long HashRangeStart,
    long HashRangeEnd);
