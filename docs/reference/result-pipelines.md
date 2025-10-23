# Result Pipeline Reference

`Result<T>` is the primary abstraction for representing success or failure. This reference enumerates the factory helpers, combinators, orchestration utilities, and metadata APIs that shape railway-oriented workflows.

## Creating results

- `Result.Ok<T>(T value)` / `Go.Ok(value)` wrap the value in a success.
- `Result.Fail<T>(Error? error)` / `Go.Err<T>(Error? error)` create failures (null defaults to `Error.Unspecified`). `Go.Err<T>(string message, string? code = null)` and `Go.Err<T>(Exception exception, string? code = null)` shortcut common cases.
- `Result.FromOptional<T>(Optional<T> optional, Func<Error> errorFactory)` lifts optionals into results.
- `Result.Try(Func<T> operation, Func<Exception, Error?>? errorFactory = null)` and `Result.TryAsync(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default, Func<Exception, Error?>? errorFactory = null)` capture exceptions as `Error` values (cancellations become `Error.Canceled`).

```csharp
var ok = Go.Ok(42);
var failure = Go.Err<int>("validation failed", ErrorCodes.Validation);
var hydrated = await Result.TryAsync(ct => repository.LoadAsync(id, ct), ct);
var forced = Result.FromOptional(Optional<string>.None(), () => Error.From("missing", ErrorCodes.Validation));
```

## Inspecting and extracting state

- `result.IsSuccess` / `result.IsFailure`
- `result.TryGetValue(out T value)` / `result.TryGetError(out Error error)`
- `result.Switch(Action<T> onSuccess, Action<Error> onFailure)` and `result.Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)`
- `result.SwitchAsync(...)` / `result.MatchAsync(...)`
- `result.ValueOr(T fallback)` / `result.ValueOr(Func<Error, T> factory)` / `result.ValueOrThrow()` (throws `ResultException`)
- `result.ToOptional()` converts to `Optional<T>`; tuple deconstruction `(value, error)` is supported for legacy interop.

## Synchronous combinators

- Execution flow: `Functional.Then`, `Functional.Recover`, `Functional.Finally`
- Mapping & side-effects: `Functional.Map`, `Functional.Tap`, `Functional.Tee`, `Functional.OnSuccess`, `Functional.OnFailure`, `Functional.TapError`
- Validation: `Functional.Ensure` (and LINQ aliases `Where`, `Select`, `SelectMany`)

```csharp
var outcome = Go.Ok(request)
    .Ensure(r => !string.IsNullOrWhiteSpace(r.Email))
    .Then(SendEmail)
    .Tap(response => audit.Log(response))
    .Map(response => response.MessageId)
    .Recover(_ => Go.Ok("fallback"));
```

## Async combinators

Every async variation accepts a `CancellationToken` and normalises cancellations to `Error.Canceled`:

- `Functional.ThenAsync` overloads bridge sync→async, async→sync, and async→async pipelines.
- `Functional.MapAsync` transforms values with synchronous or asynchronous mappers.
- `Functional.TapAsync` / `Functional.TeeAsync` execute side-effects without altering the pipeline (sync or async).
- `Functional.RecoverAsync` retries failures with synchronous or asynchronous recovery logic.
- `Functional.EnsureAsync` validates successful values asynchronously.
- `Functional.FinallyAsync` awaits success/failure continuations (sync or async callbacks).

## Collection helpers

- `Result.Sequence` / `Result.SequenceAsync` aggregate successes from `IEnumerable<Result<T>>` and `IAsyncEnumerable<Result<T>>`.
- `Result.Traverse` / `Result.TraverseAsync` project values through selectors that return `Result<T>`.
- `Result.Group`, `Result.Partition`, and `Result.Window` reshape collections while short-circuiting on the first failure.
- `Result.MapStreamAsync` projects asynchronous streams into result streams, aborting after the first failure.

All helpers propagate the first encountered error and respect cancellation tokens.

## Streaming and channels

- `IAsyncEnumerable<Result<T>>.ToChannelAsync(ChannelWriter<Result<T>> writer, CancellationToken)` and `ChannelReader<Result<T>>.ReadAllAsync(CancellationToken)` bridge result streams with `System.Threading.Channels`.
- `Result.FanInAsync` / `Result.FanOutAsync` merge or broadcast result streams across channel writers.
- `Result.WindowAsync` batches successful values into fixed-size windows; `Result.PartitionAsync` splits streams using a predicate.

Writers are completed automatically (with the originating error when appropriate) to prevent consumer deadlocks.

## Parallel orchestration and retries

- `Result.WhenAll` executes result-aware operations concurrently, applying the supplied `ResultExecutionPolicy` (retries + compensation) to each step.
- `Result.WhenAny` resolves once the first success arrives, compensating secondary successes and aggregating errors when every branch fails.
- `Result.RetryWithPolicyAsync` runs a delegate under a retry/compensation policy, surfacing structured failure metadata when attempts are exhausted.
- `Result.TieredFallbackAsync` evaluates `ResultFallbackTier<T>` instances sequentially; strategies within a tier run concurrently and cancel once a peer succeeds. Metadata keys (`fallbackTier`, `tierIndex`, `strategyIndex`) are attached to failures for observability.
- `ResultFallbackTier<T>.From(...)` adapts synchronous or asynchronous delegates into tier definitions without manually handling `ResultPipelineStepContext`.

### Tiered fallback example

```csharp
var policy = ResultExecutionPolicy.None.WithRetry(
    ResultRetryPolicy.Exponential(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(200)));

var tiers = new[]
{
    ResultFallbackTier<HttpResponseMessage>.From(
        "primary",
        ct => TrySendAsync(primaryClient, payload, ct)),
    new ResultFallbackTier<HttpResponseMessage>(
        "regional",
        new Func<ResultPipelineStepContext, CancellationToken, ValueTask<Result<HttpResponseMessage>>>[]
        {
            (ctx, ct) => TrySendAsync(euClient, payload, ct),
            (ctx, ct) => TrySendAsync(apacClient, payload, ct)
        })
};

var response = await Result.TieredFallbackAsync(tiers, policy, cancellationToken);

if (response.IsFailure && response.Error!.Metadata.TryGetValue("fallbackTier", out var tier))
{
    logger.LogWarning("All strategies in tier {Tier} failed: {Error}", tier, response.Error);
}
```

### ErrGroup integration

```csharp
using var group = new ErrGroup();
var retryPolicy = ResultExecutionPolicy.None.WithRetry(
    ResultRetryPolicy.FixedDelay(maxAttempts: 3, delay: TimeSpan.FromSeconds(1)));

group.Go((ctx, ct) =>
{
    return Result.RetryWithPolicyAsync(async (_, token) =>
    {
        var response = await client.SendAsync(request, token);
        return response.IsSuccessStatusCode
            ? Result.Ok(Go.Unit.Value)
            : Result.Fail<Unit>(Error.From("HTTP failure", ErrorCodes.Validation));
    }, retryPolicy, ct, ctx.TimeProvider);
}, stepName: "ship-order", policy: retryPolicy);

var completion = await group.WaitAsync(cancellationToken);
```

## Error metadata

- `Error.WithMetadata(string key, object? value)` / `Error.WithMetadata(IEnumerable<KeyValuePair<string, object?>> metadata)`
- `Error.TryGetMetadata<T>(string key, out T value)`
- `Error.WithCode(string? code)` / `Error.WithCause(Exception? cause)`
- Factory helpers: `Error.From`, `Error.FromException`, `Error.Canceled`, `Error.Timeout`, `Error.Unspecified`, `Error.Aggregate`

```csharp
var result = Go.Ok(user)
    .Ensure(
        predicate: u => u.Age >= 18,
        errorFactory: u => Error.From("age must be >= 18", ErrorCodes.Validation)
            .WithMetadata("age", u.Age)
            .WithMetadata("userId", u.Id));

if (result.IsFailure && result.Error!.TryGetMetadata<int>("age", out var age))
{
    logger.LogWarning("Rejected under-age user {UserId} ({Age})", result.Error.Metadata["userId"], age);
}
```

## Cancellation handling

- `Error.Canceled` represents cancellation captured via Hugo APIs and carries the originating token (when available) under `"cancellationToken"`.
- Async combinators convert `OperationCanceledException` into `Error.Canceled` so downstream callers can branch consistently.

## Diagnostics

When `GoDiagnostics` is configured, result creation increments:

- `result.successes`
- `result.failures`

Side-effect helpers such as `TapError` also contribute to `result.failures`, making it easy to correlate result pipelines with observability platforms.
