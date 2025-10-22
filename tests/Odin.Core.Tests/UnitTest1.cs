using Odin.Core;
using Shouldly;

namespace Odin.Core.Tests;

public class HashingUtilitiesTests
{
    [Theory]
    [InlineData("workflow-alpha", 16)]
    [InlineData("workflow-beta", 512)]
    [InlineData("workflow-gamma", 1024)]
    public void CalculateShardId_ReturnsValueWithinRange(string workflowId, int shardCount)
    {
        var shard = HashingUtilities.CalculateShardId(workflowId, shardCount);

        shard.ShouldBeGreaterThanOrEqualTo(0);
        shard.ShouldBeLessThan(shardCount);
    }

    [Fact]
    public void CalculateShardId_IsDeterministic()
    {
        const string workflowId = "deterministic-workflow";

        var first = HashingUtilities.CalculateShardId(workflowId);
        var second = HashingUtilities.CalculateShardId(workflowId);

        second.ShouldBe(first);
    }

    [Fact]
    public void CalculateShardId_ThrowsWhenWorkflowIdMissing()
    {
        Should.Throw<ArgumentException>(() => HashingUtilities.CalculateShardId(string.Empty));
    }

    [Fact]
    public void CalculatePartitionHash_ReturnsValueWithinRange()
    {
        var partition = HashingUtilities.CalculatePartitionHash("queue-a", 32);

        partition.ShouldBeGreaterThanOrEqualTo(0);
        partition.ShouldBeLessThan(32);
    }

    [Fact]
    public void CalculatePartitionHash_IsDeterministic()
    {
        const string queueName = "critical-tasks";

        var first = HashingUtilities.CalculatePartitionHash(queueName);
        var second = HashingUtilities.CalculatePartitionHash(queueName);

        second.ShouldBe(first);
    }

    [Fact]
    public void CalculatePartitionHash_ThrowsWhenPartitionCountInvalid()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => HashingUtilities.CalculatePartitionHash("queue", 0));
    }

    [Fact]
    public void GenerateHash_ReturnsBase64EncodedString()
    {
        const string input = "workflow-hash";

        var hash = HashingUtilities.GenerateHash(input);

        hash.ShouldNotBeNull();
        hash.ShouldNotBeEmpty();

        Span<byte> buffer = stackalloc byte[hash.Length];
        Convert.TryFromBase64String(hash, buffer, out var bytesWritten).ShouldBeTrue();
        bytesWritten.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void GenerateHash_IsDeterministic()
    {
        const string input = "consistent-value";

        var first = HashingUtilities.GenerateHash(input);
        var second = HashingUtilities.GenerateHash(input);

        second.ShouldBe(first);
    }

    [Fact]
    public void GenerateHash_ThrowsWhenValueMissing()
    {
        Should.Throw<ArgumentException>(() => HashingUtilities.GenerateHash(string.Empty));
    }
}
