using System.Security.Cryptography;
using System.Text;

namespace Odin.Core;

/// <summary>
/// Utility methods for workflow ID hashing and shard calculation
/// </summary>
public static class HashingUtilities
{
    private const int DefaultShardCount = 512;

    /// <summary>
    /// Calculates the shard ID for a workflow ID using consistent hashing
    /// </summary>
    /// <param name="workflowId">The workflow identifier</param>
    /// <param name="shardCount">Total number of shards (default: 512)</param>
    /// <returns>The shard ID (0-based index)</returns>
    public static int CalculateShardId(string workflowId, int shardCount = DefaultShardCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        ArgumentOutOfRangeException.ThrowIfLessThan(shardCount, 1);

        // Use SHA256 for consistent hashing
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(workflowId));

        // Convert first 8 bytes to a long
        var hashValue = BitConverter.ToInt64(hashBytes, 0);

        // Ensure positive value and calculate modulo
        var positiveHash = hashValue == long.MinValue ? long.MaxValue : Math.Abs(hashValue);

        return (int)(positiveHash % shardCount);
    }

    /// <summary>
    /// Calculates the partition hash for task queue distribution
    /// </summary>
    /// <param name="taskQueueName">The task queue name</param>
    /// <param name="partitionCount">Total number of partitions (default: 16)</param>
    /// <returns>The partition hash</returns>
    public static int CalculatePartitionHash(string taskQueueName, int partitionCount = 16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskQueueName);
        ArgumentOutOfRangeException.ThrowIfLessThan(partitionCount, 1);

        var hashCode = taskQueueName.GetHashCode();
        var positiveHash = hashCode == int.MinValue ? int.MaxValue : Math.Abs(hashCode);

        return positiveHash % partitionCount;
    }

    /// <summary>
    /// Generates a deterministic hash for a value
    /// </summary>
    /// <param name="value">The value to hash</param>
    /// <returns>A deterministic hash string</returns>
    public static string GenerateHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(hashBytes);
    }
}
