using Dapper;
using Npgsql;
using Odin.Core;
using Odin.Persistence.Repositories;
using Shouldly;

namespace Odin.Integration.Tests;

[Collection("PostgresIntegration")]
public sealed class ShardRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly PostgresFixture _fixture = fixture;
    private ShardRepository? _repository;

    public async ValueTask InitializeAsync()
    {
        _fixture.EnsureDockerIsRunning();

        _repository ??= _fixture.CreateShardRepository();
        await _fixture.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RenewLeaseAsync_ForCurrentOwner_ExtendsExpiry()
    {
        const int shardId = 7;
        const string owner = "history-service-a";

        var acquired = await Repository.AcquireLeaseAsync(
            shardId,
            owner,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        acquired.IsSuccess.ShouldBeTrue(acquired.Error?.Message ?? "AcquireLeaseAsync failed");
        var originalExpiry = acquired.Value.LeaseExpiry;

        var renewed = await Repository.RenewLeaseAsync(
            shardId,
            owner,
            TimeSpan.FromSeconds(15),
            TestContext.Current.CancellationToken);

        renewed.IsSuccess.ShouldBeTrue(renewed.Error?.Message ?? "RenewLeaseAsync failed");
        renewed.Value.LeaseExpiry.ShouldBeGreaterThan(originalExpiry);
        renewed.Value.OwnerHost.ShouldBe(owner);

        var lease = await Repository.GetLeaseAsync(shardId, TestContext.Current.CancellationToken);
        lease.IsSuccess.ShouldBeTrue(lease.Error?.Message ?? "GetLeaseAsync failed after renewal");
        lease.Value.ShouldNotBeNull();
        lease.Value!.OwnerHost.ShouldBe(owner);
        lease.Value.LeaseExpiry.ShouldBe(renewed.Value.LeaseExpiry);
    }

    [Fact]
    public async Task RenewLeaseAsync_WhenOwnedByDifferentHost_ReturnsUnavailable()
    {
        const int shardId = 9;

        var acquired = await Repository.AcquireLeaseAsync(
            shardId,
            "owner-a",
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        acquired.IsSuccess.ShouldBeTrue(acquired.Error?.Message ?? "AcquireLeaseAsync failed");

        var renewed = await Repository.RenewLeaseAsync(
            shardId,
            "owner-b",
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        renewed.IsFailure.ShouldBeTrue("Renewing lease with different owner should fail");
        renewed.Error.ShouldNotBeNull();
        renewed.Error.Code.ShouldBe(OdinErrorCodes.ShardUnavailable);
    }

    [Fact]
    public async Task RenewLeaseAsync_WhenLeaseExpired_ReturnsUnavailable()
    {
        const int shardId = 11;
        const string owner = "owner-expire";

        var acquired = await Repository.AcquireLeaseAsync(
            shardId,
            owner,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        acquired.IsSuccess.ShouldBeTrue(acquired.Error?.Message ?? "AcquireLeaseAsync failed");

        await ForceExpireLeaseAsync(shardId);

        var renewed = await Repository.RenewLeaseAsync(
            shardId,
            owner,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        renewed.IsFailure.ShouldBeTrue("Renewing expired lease should fail");
        renewed.Error.ShouldNotBeNull();
        renewed.Error.Code.ShouldBe(OdinErrorCodes.ShardUnavailable);
    }

    [Fact]
    public async Task ReleaseLeaseAsync_ForCurrentOwner_ClearsLease()
    {
        const int shardId = 13;
        const string owner = "history-service-release";

        var acquired = await Repository.AcquireLeaseAsync(
            shardId,
            owner,
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        acquired.IsSuccess.ShouldBeTrue(acquired.Error?.Message ?? "AcquireLeaseAsync failed");

        var release = await Repository.ReleaseLeaseAsync(
            shardId,
            owner,
            TestContext.Current.CancellationToken);

        release.IsSuccess.ShouldBeTrue(release.Error?.Message ?? "ReleaseLeaseAsync failed");

        var lease = await Repository.GetLeaseAsync(
            shardId,
            TestContext.Current.CancellationToken);

        lease.IsSuccess.ShouldBeTrue(lease.Error?.Message ?? "GetLeaseAsync failed after release");
        lease.Value.ShouldBeNull();

        var reacquired = await Repository.AcquireLeaseAsync(
            shardId,
            "history-service-new-owner",
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        reacquired.IsSuccess.ShouldBeTrue(reacquired.Error?.Message ?? "AcquireLeaseAsync failed after release");
        reacquired.Value.OwnerHost.ShouldBe("history-service-new-owner");
    }

    [Fact]
    public async Task ReleaseLeaseAsync_WhenOwnedByDifferentHost_ReturnsUnavailable()
    {
        const int shardId = 15;

        var acquired = await Repository.AcquireLeaseAsync(
            shardId,
            "owner-release-a",
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        acquired.IsSuccess.ShouldBeTrue(acquired.Error?.Message ?? "AcquireLeaseAsync failed");

        var release = await Repository.ReleaseLeaseAsync(
            shardId,
            "owner-release-b",
            TestContext.Current.CancellationToken);

        release.IsFailure.ShouldBeTrue("Releasing lease for different owner should fail");
        release.Error.ShouldNotBeNull();
        release.Error.Code.ShouldBe(OdinErrorCodes.ShardUnavailable);
    }

    private async Task ForceExpireLeaseAsync(int shardId)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            UPDATE history_shards
            SET lease_expires_at = NOW() - INTERVAL '1 second'
            WHERE shard_id = @ShardId;
            """;

        var rows = await connection.ExecuteAsync(sql, new { ShardId = shardId });
        rows.ShouldBeGreaterThan(0, "Failed to update shard lease expiry for test setup.");
    }

    private ShardRepository Repository
        => _repository ?? throw new InvalidOperationException("Shard repository has not been initialized.");
}
