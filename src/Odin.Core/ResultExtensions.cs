using Hugo;
using static Hugo.Go;

namespace Odin.Core;

/// <summary>
/// Extension methods for working with Hugo Result types in Odin
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Combines multiple results into a single result containing a list of values
    /// </summary>
    public static Result<IReadOnlyList<T>> Combine<T>(this IEnumerable<Result<T>> results)
    {
        var values = new List<T>();
        var errors = new List<Error>();

        foreach (var result in results)
        {
            if (result.IsSuccess && result.Value is not null)
            {
                values.Add(result.Value);
            }
            else if (result.IsFailure && result.Error is not null)
            {
                errors.Add(result.Error);
            }
        }

        if (errors.Count > 0)
        {
            var combinedError = Error.From(
                $"Multiple failures occurred: {string.Join(", ", errors.Select(e => e.Message))}",
                "MULTIPLE_ERRORS"
            ).WithMetadata(new Dictionary<string, object?>
            {
                ["errors"] = errors,
                ["errorCount"] = errors.Count
            });

            return Result.Fail<IReadOnlyList<T>>(combinedError);
        }

        return Result.Ok<IReadOnlyList<T>>(values);
    }

    /// <summary>
    /// Maps a result value to a workflow execution result
    /// </summary>
    public static Result<TOutput> ToWorkflowResult<TInput, TOutput>(
        this Result<TInput> input,
        Func<TInput, TOutput> mapper)
    {
        return input.Map(mapper);
    }

    /// <summary>
    /// Converts a task result to a Result type, catching exceptions
    /// </summary>
    public static async Task<Result<T>> ToResult<T>(
        this Task<T> task,
        string context = "Operation")
    {
        try
        {
            var value = await task.ConfigureAwait(false);
            return Result.Ok(value);
        }
        catch (OperationCanceledException ex)
        {
            return Result.Fail<T>(Error.Canceled(token: ex.CancellationToken));
        }
        catch (Exception ex)
        {
            return Result.Fail<T>(Error.FromException(ex).WithMetadata(new Dictionary<string, object?>
            {
                ["context"] = context
            }));
        }
    }

    /// <summary>
    /// Converts a void task to a Result, catching exceptions
    /// </summary>
    public static async Task<Result<Unit>> ToResult(
        this Task task,
        string context = "Operation")
    {
        try
        {
            await task.ConfigureAwait(false);
            return Result.Ok(Unit.Value);
        }
        catch (OperationCanceledException ex)
        {
            return Result.Fail<Unit>(Error.Canceled(token: ex.CancellationToken));
        }
        catch (Exception ex)
        {
            return Result.Fail<Unit>(Error.FromException(ex).WithMetadata(new Dictionary<string, object?>
            {
                ["context"] = context
            }));
        }
    }

    /// <summary>
    /// Executes an action if the result is successful
    /// </summary>
    public static Result<T> OnSuccess<T>(this Result<T> result, Action<T> action)
    {
        if (result.IsSuccess && result.Value is not null)
        {
            action(result.Value);
        }
        return result;
    }

    /// <summary>
    /// Executes an async action if the result is successful
    /// </summary>
    public static async Task<Result<T>> OnSuccessAsync<T>(
        this Result<T> result,
        Func<T, Task> action)
    {
        if (result.IsSuccess && result.Value is not null)
        {
            await action(result.Value).ConfigureAwait(false);
        }
        return result;
    }

    /// <summary>
    /// Executes an action if the result is a failure
    /// </summary>
    public static Result<T> OnFailure<T>(this Result<T> result, Action<Error> action)
    {
        if (result.IsFailure && result.Error is not null)
        {
            action(result.Error);
        }
        return result;
    }

    /// <summary>
    /// Validates a result value and returns failure if validation fails
    /// </summary>
    public static Result<T> Validate<T>(
        this Result<T> result,
        Func<T, bool> predicate,
        string errorMessage)
    {
        if (result.IsFailure)
        {
            return result;
        }

        if (result.Value is null || !predicate(result.Value))
        {
            return Result.Fail<T>(Error.From(errorMessage, ErrorCodes.Validation));
        }

        return result;
    }
}

/// <summary>
/// Core utilities and extensions for Odin
/// </summary>
public static class OdinExtensions
{
    // Additional Odin-specific extensions
}
