using Hugo;
using Microsoft.Extensions.Logging.Abstractions;
using Odin.Core;
using Shouldly;
using Xunit;
using static Hugo.Go;

namespace Odin.Core.Tests;

public class GoHelpersTests
{
    [Fact]
    public async Task FanOutAsync_AllOperationsSucceed_ReturnsAggregatedValues()
    {
        var operations = new List<Func<CancellationToken, Task<Result<int>>>>
        {
            async ct =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
                return Result.Ok(1);
            },
            _ => Task.FromResult(Result.Ok(2))
        };

        var result = await GoHelpers.FanOutAsync(operations, cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new[] { 1, 2 });
    }

    [Fact]
    public async Task FanOutAsync_WhenAnyOperationFails_PropagatesError()
    {
        var operations = new List<Func<CancellationToken, Task<Result<int>>>>
        {
            _ => Task.FromResult(Result.Fail<int>(Error.From("boom", "FAILURE"))),
            _ => Task.FromResult(Result.Ok(2))
        };

        var result = await GoHelpers.FanOutAsync(operations, cancellationToken: TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe("FAILURE");
    }

    [Fact]
    public async Task RaceAsync_ReturnsFirstSuccessfulResult()
    {
        var operations = new List<Func<CancellationToken, Task<Result<string>>>>
        {
            async ct =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
                return Result.Ok("winner");
            },
            async ct =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                return Result.Fail<string>(Error.From("slow failure", "SLOW_FAIL"));
            }
        };

        var result = await GoHelpers.RaceAsync(operations, cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("winner");
    }

    [Fact]
    public async Task WithTimeoutAsync_WhenOperationExceedsTimeout_ReturnsTimeoutError()
    {
        var result = await GoHelpers.WithTimeoutAsync(
            async ct =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                return Result.Ok("eventual-success");
            },
            TimeSpan.FromMilliseconds(50),
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe(OdinErrorCodes.Timeout);
    }

    [Fact]
    public async Task RetryAsync_RetriesUntilOperationSucceeds()
    {
        var attempts = new List<int>();

        var result = await GoHelpers.RetryAsync(
            (attempt, _) =>
            {
                attempts.Add(attempt);

                if (attempt < 3)
                {
                    return Task.FromResult(Result.Fail<int>(Error.From($"attempt-{attempt}", "RETRY")));
                }

                return Task.FromResult(Result.Ok(42));
            },
            maxAttempts: 5,
            initialDelay: TimeSpan.FromMilliseconds(10),
            logger: NullLogger.Instance,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        attempts.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task RetryAsync_WhenAllAttemptsFail_ReturnsLastError()
    {
        var attempts = new List<int>();

        var result = await GoHelpers.RetryAsync(
            (attempt, _) =>
            {
                attempts.Add(attempt);
                return Task.FromResult(Result.Fail<int>(Error.From("always-fails", "RETRY")));
            },
            maxAttempts: 3,
            initialDelay: TimeSpan.FromMilliseconds(10),
            logger: NullLogger.Instance,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldNotBeNull();
        result.Error.Code.ShouldBe("RETRY");
        attempts.ShouldBe(new[] { 1, 2, 3 });
    }
}
