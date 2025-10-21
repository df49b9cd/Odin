using Hugo;
using static Hugo.Go;
using Microsoft.Extensions.Logging;

namespace Odin.Core;

/// <summary>
/// Utility methods for working with Hugo Go primitives in Odin
/// </summary>
public static class GoHelpers
{
    /// <summary>
    /// Executes multiple operations concurrently using ErrGroup and returns combined results
    /// </summary>
    public static async Task<Result<IReadOnlyList<T>>> FanOutAsync<T>(
        IEnumerable<Func<CancellationToken, Task<Result<T>>>> operations,
        CancellationToken cancellationToken = default)
    {
        using var errGroup = new ErrGroup();
        var results = new List<Result<T>>();
        var resultsLock = new object();

        foreach (var operation in operations)
        {
            var op = operation; // Capture for closure
            errGroup.Go(async ct =>
            {
                var result = await op(ct).ConfigureAwait(false);
                lock (resultsLock)
                {
                    results.Add(result);
                }
                return result.IsSuccess ? Result.Ok(Unit.Value) : Result.Fail<Unit>(result.Error!);
            });
        }

        var groupResult = await errGroup.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        if (groupResult.IsFailure)
        {
            return Result.Fail<IReadOnlyList<T>>(groupResult.Error!);
        }

        return results.Combine();
    }

    /// <summary>
    /// Executes multiple operations concurrently and returns the first successful result
    /// </summary>
    public static async Task<Result<T>> RaceAsync<T>(
        IEnumerable<Func<CancellationToken, Task<Result<T>>>> operations,
        CancellationToken cancellationToken = default)
    {
        var operationList = operations.ToList();
        if (operationList.Count == 0)
        {
            return Result.Fail<T>(Error.From("No operations provided", "VALIDATION_ERROR"));
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tasks = operationList.Select(op => Task.Run(async () => await op(cts.Token), cts.Token)).ToList();

        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(completed);

            try
            {
                var result = await completed.ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    cts.Cancel(); // Cancel remaining operations
                    return result;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Result.Fail<T>(Error.Canceled(token: cancellationToken));
            }
            catch (Exception ex)
            {
                if (tasks.Count == 0)
                {
                    return Result.Fail<T>(Error.FromException(ex));
                }
            }
        }

        return Result.Fail<T>(Error.From("All operations failed", "MULTIPLE_ERRORS"));
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
        
        // Use Task.Delay with TimeProvider
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
        var delay = initialDelay ?? TimeSpan.FromMilliseconds(100);
        var provider = timeProvider ?? TimeProvider.System;
        Error? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = await operation(attempt, cancellationToken).ConfigureAwait(false);
                
                if (result.IsSuccess)
                {
                    if (attempt > 1)
                    {
                        logger?.LogInformation(
                            "Operation succeeded on attempt {Attempt} of {MaxAttempts}",
                            attempt,
                            maxAttempts);
                    }
                    return result;
                }

                lastError = result.Error;
                
                if (attempt < maxAttempts)
                {
                    var waitTime = delay * Math.Pow(2, attempt - 1);
                    logger?.LogWarning(
                        "Operation failed on attempt {Attempt} of {MaxAttempts}: {Error}. Retrying after {WaitMs}ms",
                        attempt,
                        maxAttempts,
                        lastError?.Message,
                        waitTime.TotalMilliseconds);
                    
                    await Task.Delay(waitTime, provider, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return Result.Fail<T>(Error.Canceled(token: cancellationToken));
            }
            catch (Exception ex)
            {
                lastError = Error.FromException(ex);
                
                if (attempt < maxAttempts)
                {
                    var waitTime = delay * Math.Pow(2, attempt - 1);
                    logger?.LogWarning(
                        ex,
                        "Operation threw exception on attempt {Attempt} of {MaxAttempts}. Retrying after {WaitMs}ms",
                        attempt,
                        maxAttempts,
                        waitTime.TotalMilliseconds);
                    
                    await Task.Delay(waitTime, provider, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        logger?.LogError(
            "Operation failed after {MaxAttempts} attempts: {Error}",
            maxAttempts,
            lastError?.Message);

        return Result.Fail<T>(lastError ?? Error.Unspecified());
    }
}
