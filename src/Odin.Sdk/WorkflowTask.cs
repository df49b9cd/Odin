using Hugo;

namespace Odin.Sdk;

/// <summary>
/// Represents a unit of workflow execution work to be processed by the worker runtime.
/// </summary>
public sealed record WorkflowTask(
    string Namespace,
    string WorkflowId,
    string RunId,
    string TaskQueue,
    string WorkflowType,
    object? Input,
    IReadOnlyDictionary<string, string>? Metadata = null,
    DateTimeOffset? StartedAt = null,
    long InitialLogicalClock = 0,
    int ReplayCount = 0,
    IDeterministicStateStore? StateStore = null,
    TimeProvider? TimeProvider = null);
