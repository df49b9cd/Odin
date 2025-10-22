using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Hugo;
using Odin.Core;

namespace Odin.Sdk;

/// <summary>
/// Options used to initialise the workflow runtime scope.
/// </summary>
public sealed record WorkflowRuntimeOptions
{
    public required string Namespace { get; init; }
    public required string WorkflowId { get; init; }
    public required string RunId { get; init; }
    public required string TaskQueue { get; init; }
    public string? ScheduleId { get; init; }
    public string? ScheduleGroup { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public long InitialLogicalClock { get; init; }
    public int ReplayCount { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public IDeterministicStateStore? StateStore { get; init; }
    public JsonSerializerOptions? SerializerOptions { get; init; }
}

/// <summary>
/// Represents the active workflow runtime scope. Disposing restores the previous scope.
/// </summary>
public sealed class WorkflowRuntimeScope : IDisposable
{
    private readonly WorkflowRuntimeState? _previousState;
    private bool _disposed;

    internal WorkflowRuntimeScope(WorkflowRuntimeState state, WorkflowRuntimeState? previousState)
    {
        State = state;
        _previousState = previousState;
    }

    internal WorkflowRuntimeState State { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WorkflowRuntime.PopScope(_previousState);
    }
}

internal sealed record WorkflowRuntimeState(
    WorkflowExecutionContext Context,
    DeterministicEffectStore EffectStore,
    VersionGate VersionGate,
    IDeterministicStateStore StateStore,
    JsonSerializerOptions SerializerOptions);

/// <summary>
/// Provides access to the ambient workflow execution context, deterministic effect store, and version gate.
/// </summary>
public static class WorkflowRuntime
{
    private static readonly AsyncLocal<WorkflowRuntimeState?> _current = new();

    /// <summary>
    /// Initialises a new runtime scope using the supplied options.
    /// </summary>
    public static WorkflowRuntimeScope Initialize(WorkflowRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        IEnumerable<KeyValuePair<string, string>> metadata = options.Metadata is { Count: > 0 } dict
            ? dict
            : Array.Empty<KeyValuePair<string, string>>();

        var timeProvider = options.TimeProvider;
        var startedAt = options.StartedAt;

        var context = new WorkflowExecutionContext(
            options.Namespace,
            options.WorkflowId,
            options.RunId,
            options.TaskQueue,
            options.ScheduleId ?? string.Empty,
            options.ScheduleGroup ?? string.Empty,
            metadata,
            timeProvider,
            startedAt,
            options.InitialLogicalClock,
            options.ReplayCount);

        var stateStore = options.StateStore ?? new InMemoryDeterministicStateStore();
        var serializerOptions = options.SerializerOptions ?? JsonOptions.Default;

        var effectStore = new DeterministicEffectStore(stateStore, timeProvider, serializerOptions);
        var versionGate = new VersionGate(stateStore, timeProvider, serializerOptions);

        return PushScope(new WorkflowRuntimeState(context, effectStore, versionGate, stateStore, serializerOptions));
    }

    internal static WorkflowRuntimeScope PushScope(WorkflowRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var scope = new WorkflowRuntimeScope(state, _current.Value);
        _current.Value = state;
        return scope;
    }

    internal static void PopScope(WorkflowRuntimeState? previous)
        => _current.Value = previous;

    /// <summary>
    /// Indicates whether a workflow runtime context is available.
    /// </summary>
    public static bool HasCurrent => _current.Value is not null;

    /// <summary>
    /// Gets the ambient workflow execution context.
    /// </summary>
    public static WorkflowExecutionContext Context => EnsureState().Context;

    /// <summary>
    /// Gets the deterministic effect store for the current workflow.
    /// </summary>
    public static DeterministicEffectStore Effects => EnsureState().EffectStore;

    /// <summary>
    /// Gets the version gate for the current workflow.
    /// </summary>
    public static VersionGate VersionGate => EnsureState().VersionGate;

    /// <summary>
    /// Gets the deterministic state store backing the current workflow.
    /// </summary>
    public static IDeterministicStateStore StateStore => EnsureState().StateStore;

    /// <summary>
    /// Gets the JSON serialiser options used for deterministic capture.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions => EnsureState().SerializerOptions;

    /// <summary>
    /// Gets the <see cref="TimeProvider"/> associated with the current workflow.
    /// </summary>
    public static TimeProvider TimeProvider => Context.TimeProvider;

    /// <summary>
    /// Attempts to resolve a metadata value from the current workflow context.
    /// </summary>
    public static bool TryGetMetadata(string key, out string? value)
        => Context.TryGetMetadata(key, out value);

    /// <summary>
    /// Captures a deterministic side-effect using the ambient effect store.
    /// </summary>
    public static Task<Result<T>> CaptureAsync<T>(
        string effectId,
        Func<CancellationToken, Task<Result<T>>> effect,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(effectId);
        ArgumentNullException.ThrowIfNull(effect);
        return Effects.CaptureAsync(effectId, effect, cancellationToken);
    }

    /// <summary>
    /// Captures a deterministic side-effect using the ambient effect store.
    /// </summary>
    public static Task<Result<T>> CaptureAsync<T>(
        string effectId,
        Func<Task<Result<T>>> effect,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(effectId);
        ArgumentNullException.ThrowIfNull(effect);
        return Effects.CaptureAsync(effectId, effect, cancellationToken);
    }

    /// <summary>
    /// Captures a deterministic side-effect using the ambient effect store.
    /// </summary>
    public static Task<Result<T>> CaptureAsync<T>(
        string effectId,
        Func<Result<T>> effect)
    {
        ArgumentNullException.ThrowIfNull(effectId);
        ArgumentNullException.ThrowIfNull(effect);
        return Effects.CaptureAsync(effectId, effect);
    }

    /// <summary>
    /// Requires a workflow version, recording it if it has not been seen before.
    /// </summary>
    public static Result<VersionDecision> RequireVersion(
        string changeId,
        int minSupportedVersion,
        int maxSupportedVersion,
        Func<VersionGateContext, int>? initialVersionProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(changeId);
        if (minSupportedVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSupportedVersion), minSupportedVersion, "Minimum version must be positive.");
        }

        if (maxSupportedVersion < minSupportedVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSupportedVersion), maxSupportedVersion, "Maximum version must be greater than or equal to the minimum.");
        }

        Func<VersionGateContext, int> provider = initialVersionProvider ?? (_ => minSupportedVersion);
        return VersionGate.Require(changeId, minSupportedVersion, maxSupportedVersion, provider);
    }

    private static WorkflowRuntimeState EnsureState()
    {
        var state = _current.Value;
        return state is null
            ? throw new InvalidOperationException("No workflow runtime scope is active. Initialise the workflow runtime before executing workflows or activities.")
            : state;
    }
}
