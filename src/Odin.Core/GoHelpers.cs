using Hugo;
using Microsoft.Extensions.Logging;
using System.Threading;
using Hugo.Policies;
using static Hugo.Go;

namespace Odin.Core;

/// <summary>
/// Utility methods for working with Hugo Go primitives in Odin
/// </summary>
public static class GoHelpers
{
    /// <summary>
    /// Executes multiple operations concurrently using Result.WhenAll with optional execution policy.
    /// </summary>
    public static Task<Result<IReadOnlyList<T>>> FanOutAsync<T>(
        IEnumerable<Func<CancellationToken, Task<Result<T>>>> operations,
        ResultExecutionPolicy? policy = null,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var adapted = operations
            .Select((operation, index) =>
                operation is null
                    ? throw new ArgumentNullException(nameof(operations), $"Operation at index {index} cannot be null.")
                    : new Func<ResultPipelineStepContext, CancellationToken, ValueTask<Result<T>>>(
                        (_, ct) => new ValueTask<Result<T>>(operation(ct))));

        return Result.WhenAll(adapted, policy, cancellationToken, timeProvider);
    }

    /// <summary>
    /// Executes multiple operations concurrently and returns the first successful result via Result.WhenAny.
    /// </summary>
    public static Task<Result<T>> RaceAsync<T>(
        IEnumerable<Func<CancellationToken, Task<Result<T>>>> operations,
        ResultExecutionPolicy? policy = null,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var adapted = operations
            .Select((operation, index) =>
                operation is null
                    ? throw new ArgumentNullException(nameof(operations), $"Operation at index {index} cannot be null.")
                    : new Func<ResultPipelineStepContext, CancellationToken, ValueTask<Result<T>>>(
                        (_, ct) => new ValueTask<Result<T>>(operation(ct))));

        return Result.WhenAny(adapted, policy, cancellationToken, timeProvider);
    }

    /// <summary>
    /// Creates a timeout result if the operation doesn't complete in time
    /// </summary>
    public static async Task<Result<T>> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<Result<T>>> operation,
        TimeSpan timeout,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        var provider = timeProvider ?? TimeProvider.System;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // TODO: Move timeout orchestration into an upstream Hugo helper when the library grows a native primitive.
        // Schedule timeout using the shared time provider
        _ = Task.Delay(timeout, provider, cts.Token)
            .ContinueWith(_ => cts.Cancel(), TaskScheduler.Default);

        try
        {
            return await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Fail<T>(OdinErrors.Timeout(nameof(operation), timeout));
        }
    }

    /// <summary>
    /// Retries an operation with exponential backoff using Hugo Result type
    /// </summary>
    public static async Task<Result<T>> RetryAsync<T>(
        Func<int, CancellationToken, Task<Result<T>>> operation,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        var provider = timeProvider ?? TimeProvider.System;
        var policy = ResultExecutionBuilders.ExponentialRetryPolicy(
            maxAttempts,
            initialDelay ?? TimeSpan.FromMilliseconds(100));

        var attempt = 0;

        var finalResult = await Result.RetryWithPolicyAsync(
                async (_, ct) =>
                {
                    var currentAttempt = Interlocked.Increment(ref attempt);

                    try
                    {
                        var result = await operation(currentAttempt, ct).ConfigureAwait(false);

                        if (result.IsSuccess && currentAttempt > 1)
                        {
                            logger?.LogInformation(
                                "Operation succeeded on attempt {Attempt} of {MaxAttempts}",
                                currentAttempt,
                                maxAttempts);
                        }
                        else if (result.IsFailure && currentAttempt < maxAttempts)
                        {
                            logger?.LogWarning(
                                "Operation failed on attempt {Attempt} of {MaxAttempts}: {Error}",
                                currentAttempt,
                                maxAttempts,
                                result.Error?.Message);
                        }

                        return result;
                    }
                    catch (OperationCanceledException oce) when (oce.CancellationToken == ct)
                    {
                        return Result.Fail<T>(Error.Canceled(token: ct));
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(
                            ex,
                            "Operation threw on attempt {Attempt} of {MaxAttempts}",
                            currentAttempt,
                            maxAttempts);
                        return Result.Fail<T>(Error.FromException(ex));
                    }
                },
                policy,
                cancellationToken,
                provider)
            .ConfigureAwait(false);

        if (finalResult.IsFailure)
        {
            logger?.LogError(
                "Operation failed after {MaxAttempts} attempts: {Error}",
                maxAttempts,
                finalResult.Error?.Message);
        }

        return finalResult;
    }
}
