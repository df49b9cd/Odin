# Architecture Overview

## System Architecture

Odin is a distributed workflow orchestration platform designed following Temporal's architecture principles while leveraging Hugo's concurrency primitives.

## Core Components

### 1. Control Plane

The Control Plane serves as the stateless front door to the system.

**Responsibilities:**
- Mutual TLS termination
- Authentication and authorization
- Rate limiting and throttling
- Request routing to execution services
- Namespace CRUD operations

**Components:**
- `Odin.ControlPlane.Api`: REST API facade (port 8080)
- `Odin.ControlPlane.Grpc`: gRPC service implementing Temporal WorkflowService proto (port 7233)

### 2. Execution Engine

The Execution Engine handles workflow state management and task distribution.

**History Service (`Odin.ExecutionEngine.History`)**
- Manages sharded workflow mutable state
- Persists immutable event history
- Handles workflow execution logic
- Implements timer, transfer, replicator, and visibility task queues
- Default: 512 shards for horizontal scalability

**Matching Service (`Odin.ExecutionEngine.Matching`)**
- Partitioned task queue management
- Task lease and delivery coordination
- Worker poll dispatch
- Heartbeat tracking
- Built on `TaskQueueChannelAdapter<T>` for lease-aware delivery

**System Workers (`Odin.ExecutionEngine.SystemWorkers`)**
- Internal workflow orchestration
- Timer execution
- Retry handling
- Cleanup workflows
- History archival
- Uses Hugo's `WaitGroup` + `ErrGroup` for coordination

### 3. Persistence Layer

**Primary Store (`Odin.Persistence`)**
- PostgreSQL 14+ or MySQL 8.0.19+
- Tables:
  - `namespaces`: Multi-tenant isolation
  - `workflow_executions`: Mutable workflow state
  - `history_events`: Immutable event log
  - `task_queues`: Task distribution
  - `history_shards`: Sharding metadata
  - `visibility_records`: Searchable workflow metadata

**Visibility Store (`Odin.Visibility`)**
- SQL Advanced Visibility (Temporal v1.20+) or Elasticsearch 8.x
- Indexed search on workflow metadata
- Support for dual-write during migrations (Temporal v1.21)
- TTL-based retention policies

### 4. Worker Runtime & SDK

**Worker SDK (`Odin.Sdk`)**
- Workflow and activity definitions
- Hugo primitive integration:
  - `WaitGroup`: Lifecycle coordination
  - `ErrGroup`: Cancellable fan-out
  - Channels: State propagation
  - `Result<T>`: Pipeline-based error handling
  - `DeterministicEffectStore`: Side effect capture
  - `VersionGate`: Workflow versioning

**Worker Host (`Odin.WorkerHost`)**
- Managed .NET 10 worker service
- Workflow execution engine
- Activity heartbeat management
- History replay coordinator
- Task queue polling

## Data Flow

### Workflow Start

```
Client → Control Plane API → gRPC Service
    → History Service (shard routing)
    → Persist workflow execution + initial events
    → Create workflow task
    → Matching Service → Task Queue
```

### Workflow Task Execution

```
Worker polls Matching Service → Task lease
    → Fetch history from History Service
    → Replay workflow (deterministic)
    → Generate commands
    → Submit to History Service
    → History Service persists new events
    → Update visibility
```

### Activity Execution

```
Workflow schedules activity → History Service
    → Create activity task
    → Matching Service → Activity Task Queue
    → Worker polls and executes
    → Sends heartbeats (long-running)
    → Completes/fails
    → History Service updates state
```

## Hugo Integration

### Concurrency Patterns

**WaitGroup - Lifecycle Coordination**
```csharp
var wg = new WaitGroup();
wg.Add(workerCount);
for (int i = 0; i < workerCount; i++)
{
    Go(async () => {
        defer(() => wg.Done());
        await RunWorker();
    });
}
await wg.Wait();
```

**ErrGroup - Cancellable Fan-out**
```csharp
var eg = new ErrGroup();
foreach (var task in tasks)
{
    var t = task;
    eg.Go(async () => await ProcessTask(t));
}
var results = await eg.Wait();
```

**Channels - Task Distribution**
```csharp
var ch = Channel<WorkflowTask>(bufferSize: 100);
await Go(async () => {
    await foreach (var task in ch.Reader.ReadAllAsync(cancellationToken))
    {
        await ProcessTask(task);
    }
});
```

**Result<T> - Error Handling**
```csharp
var result = await ExecuteActivity()
    .Then(ProcessResult)
    .Recover(HandleError)
    .Ensure(Cleanup)
    .Finally(LogCompletion);
```

## Determinism Guarantees

### Workflow Determinism

Workflows must produce identical event sequences given the same history:
- No `DateTime.Now`, `Random`, or direct I/O
- Side effects captured via `DeterministicEffectStore`
- Versioning via `VersionGate` for incompatible changes

### Replay Safety

```csharp
// Workflow execution
var store = new DeterministicEffectStore();
var timestamp = store.GetOrCapture("timestamp", () => DateTime.UtcNow);

// Replay - returns captured value
var replayTimestamp = store.GetOrCapture("timestamp", () => DateTime.UtcNow);
// replayTimestamp == timestamp (guaranteed)
```

### Version Management

```csharp
var gate = new VersionGate();
if (gate.IsEnabled("new-payment-flow", version: 2))
{
    // New payment logic
    await ProcessPaymentV2();
}
else
{
    // Legacy logic for replay
    await ProcessPaymentV1();
}
```

## Observability

### Metrics (Prometheus)
- `workflow_duration_seconds{namespace, workflow_type, status}`
- `workflow_replay_count{namespace, workflow_type}`
- `task_queue_latency_seconds{queue_name, task_type}`
- `history_shard_ownership{shard_id, host}`

### Tracing (OpenTelemetry)
Attributes:
- `workflow.namespace`
- `workflow.id`
- `workflow.run_id`
- `workflow.type`
- `workflow.task_queue`
- `activity.type`
- `activity.id`

### Logging
Structured context:
- `correlation_id`
- `workflow_id`
- `activity_id`
- `namespace`
- `worker_identity`

## Security

### mTLS
- Between all service components
- Worker to control plane
- Certificate rotation via secret stores

### Namespace Isolation
- API layer enforcement
- Persistence query validation
- Separate encryption keys per namespace

### Worker Identity
- Derived from deployment context (cluster/region/pod)
- Included in visibility and audit logs
- Validated in task dispatch

## Scalability

### Sharding
- 512 default history shards
- Workflow ID hash-based routing
- Shard ownership with leases

### Task Queue Partitioning
- Hash-based partitioning by queue name
- Sticky routing for workflow tasks
- Load-based rebalancing

### Caching Strategy
- Namespace metadata (5 min TTL)
- Workflow definitions
- Shard ownership (lease-based)

## Deployment Patterns

### Single Cluster (Phase 0-1)
- All components in one cluster
- Basic SQL persistence
- In-memory dev task queues

### Multi-Instance (Phase 2)
- Load-balanced control plane
- Elasticsearch visibility
- Prometheus + Grafana

### Enterprise (Phase 3)
- Multi-tenancy with namespaces
- Cross-region replication
- Blue-green deployments
- Compliance reporting

## References

- [Temporal Service Documentation](https://docs.temporal.io/temporal-service)
- [Temporal Server Architecture](https://docs.temporal.io/temporal-service/temporal-server)
- [Temporal Persistence](https://docs.temporal.io/temporal-service/persistence)
- [Hugo Library](https://github.com/df49b9cd/Hugo)
