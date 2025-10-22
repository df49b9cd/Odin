# Hugo Durable Orchestrator (Odin) - Copilot Instructions

## Project Overview

You are working on the **Hugo Durable Orchestrator**, a Temporal/Cadence-style workflow orchestration platform built on Hugo 1.0.0 concurrency primitives. This system provides durable workflow execution, history replay, and distributed task routing while maintaining Hugo as the core worker/runtime SDK.

### Core Architecture Components

1. **Control Plane**: Stateless API gateway handling authentication, rate limiting, and request routing
2. **Execution Engine**: History service, matching service, and system workers for workflow orchestration
3. **Persistence Layer**: SQL-based storage (PostgreSQL 14+/MySQL 8.0.19+) with optional Elasticsearch
4. **Worker Runtime**: .NET 10 managed hosts using Hugo primitives for workflow execution
5. **Visibility System**: Workflow state tracking and advanced search capabilities

## Technology Stack

- **Runtime**: .NET 10
- **Core Library**: Hugo 1.0.0 (concurrency primitives and diagnostics)
- **APIs**: gRPC (primary) + REST (facade)
- **Persistence**: PostgreSQL 14+ or MySQL 8.0.19+
- **Search**: Elasticsearch 8.x or SQL advanced visibility
- **Observability**: OpenTelemetry, Prometheus, Grafana
- **Deployment**: Kubernetes, Helm, Terraform

## Hugo Integration Principles

### Import Convention
Always use static imports for Hugo:
```csharp
using static Hugo.Go;
```

### Core Primitives Usage

1. **WaitGroup**: Lifecycle coordination and synchronization
2. **ErrGroup**: Cancellable fan-out operations with error propagation
3. **Channels**: State propagation and inter-component communication
4. **TaskQueueChannelAdapter<T>**: Lease-aware task delivery with heartbeats
5. **Result<T>**: Pipeline-based error handling (Then, Recover, Ensure, Finally)
6. **DeterministicEffectStore**: Capturing side effects for replay safety
7. **VersionGate**: Managing workflow versioning and compatibility
8. **Result Execution Policies**: Prefer `Result.WhenAll`, `Result.WhenAny`, and `Result.RetryWithPolicyAsync` over custom fan-out/race/retry loops. Build policies with `ResultExecutionBuilders` and reuse Hugo compensation scopes.
9. **Functional Extensions**: Use `Then`, `Map`, `Tap`, `TapError`, `Recover`, and async counterparts for flow control instead of manual `if (result.IsFailure)` checks.

## Coding Standards

### Workflow Development

1. **Determinism is Critical**
   - Never use non-deterministic operations (DateTime.Now, Random, external I/O) directly in workflows
   - Always capture side effects through DeterministicEffectStore
   - Use VersionGate for incompatible workflow changes

2. **Error Handling Pattern**
   ```csharp
   var result = await ExecuteActivity()
       .Then(ProcessResult)
       .Recover(HandleError)
       .Ensure(Cleanup)
       .Finally(LogCompletion);
   ```

3. **Cancellation Semantics**
   - Always propagate CancellationToken through the call chain
   - Use Hugo's cancellation primitives for coordinated shutdown
   - Implement proper cleanup in deferred blocks
   - Prefer Hugo's `Result.With` helpers before introducing new timeout constructs; if a custom timeout helper is required, leave a `// TODO: Move to Hugo` note and implement it alongside Odin scaffolding.

### Activity Implementation

1. Activities can be non-deterministic (external calls, I/O operations)
2. Implement heartbeats for long-running activities
3. Use proper retry policies with exponential backoff
4. Return Result<T> for explicit error handling

### Service Implementation

1. **Naming Conventions**
   - Services: `{Feature}Service` (e.g., `HistoryService`, `MatchingService`)
   - Workers: `{Type}Worker` (e.g., `TimerWorker`, `CleanupWorker`)
   - Handlers: `{Operation}Handler` (e.g., `StartWorkflowHandler`)

2. **Dependency Injection**
   ```csharp
   services.AddSingleton<IHistoryService, HistoryService>();
   services.AddHostedService<WorkflowWorker>();
   services.AddHugoDiagnostics(config => { /* OTLP configuration */ });
   ```

3. **Result Handling**
   - Wrap disposable resources with `Result.TryAsync` + `ThenAsync` instead of bare `try/catch`.
   - Surface failures through `Error.From`/`Error.Aggregate` and log via `TapError`/`Tap`.
   - Only write bespoke helpers when Hugo lacks a primitive and annotate them with a TODO to upstream.

## API Design Guidelines

### gRPC Services
- Follow Temporal WorkflowService proto patterns
- Port 7233 for workflow service
- Implement proper streaming for long polls
- Use metadata for workflow context propagation

### REST API
- Mirror gRPC semantics exactly
- Provide OpenAPI/Swagger documentation
- Support pagination with cursor-based navigation
- Return consistent error responses

## Persistence Schema Design

### Core Tables
1. `namespaces`: Multi-tenant isolation
2. `workflow_executions`: Mutable workflow state
3. `history_events`: Immutable event log
4. `task_queues`: Task distribution
5. `history_shards`: Sharding metadata (512 shards default)
6. `visibility_records`: Searchable workflow metadata

### Indexing Strategy
- Shard by workflow_id for history
- Partition task queues by name hash
- Index visibility by namespace, status, start_time
- Implement TTL-based retention

## Observability Requirements

### Metrics (Prometheus format)
```
workflow_duration_seconds{namespace, workflow_type, status}
workflow_replay_count{namespace, workflow_type}
task_queue_latency_seconds{queue_name, task_type}
history_shard_ownership{shard_id, host}
```

### Tracing Attributes
```
workflow.namespace
workflow.id
workflow.run_id
workflow.type
workflow.task_queue
activity.type
activity.id
```

### Logging Context
Always include structured fields:
- correlation_id
- workflow_id
- activity_id
- namespace
- worker_identity

## Testing Guidelines

### Unit Tests
1. Test workflows with mocked activities
2. Verify deterministic replay with WorkflowExecutionContext
3. Test version compatibility with VersionGate
4. Validate Result<T> pipeline error handling

### Integration Tests
1. Test end-to-end workflow execution
2. Verify history replay from persisted events
3. Test task queue distribution
4. Validate visibility queries

### Determinism Tests
```csharp
[Test]
public async Task Workflow_Should_Replay_Deterministically()
{
    var history = await LoadHistoryFromDatabase();
    var result1 = await ReplayWorkflow(history);
    var result2 = await ReplayWorkflow(history);
    Assert.AreEqual(result1, result2);
}
```

## Security Considerations

1. **mTLS Requirements**
   - Between all service components
   - Worker to control plane communication
   - Certificate rotation via secret stores

2. **Namespace Isolation**
   - Enforce at API layer
   - Validate in persistence queries
   - Separate encryption keys per namespace

3. **Worker Identity**
   - Derive from deployment context (cluster/region/pod)
   - Include in visibility and audit logs
   - Validate in task dispatch

## Performance Optimization

1. **Batching**
   - Batch history events (100-500 per transaction)
   - Batch visibility updates
   - Batch task queue operations

2. **Caching**
   - Cache namespace metadata (5 min TTL)
   - Cache workflow definitions
   - Cache shard ownership (with lease)

3. **Connection Pooling**
   - SQL: 20-50 connections per service
   - gRPC: Reuse channels with proper lifecycle
   - Elasticsearch: Bulk indexing with circuit breakers

## Deployment Patterns

### Phase 0-1 (Initial Development)
- Single cluster, single region
- Basic SQL persistence
- In-memory task queues for development

### Phase 2 (Production Ready)
- Multi-instance with load balancing
- Elasticsearch for visibility
- Prometheus + Grafana monitoring

### Phase 3 (Enterprise)
- Multi-tenancy with namespace isolation
- Cross-region replication design
- Blue-green deployments
- Compliance reporting

## Common Pitfalls to Avoid

1. **Never** modify workflow logic without versioning
2. **Never** use blocking I/O in workflow code
3. **Never** share mutable state between workflows
4. **Always** handle activity timeouts explicitly
5. **Always** implement proper cleanup in Finally blocks
6. **Always** validate shard ownership before processing

## CLI Tool Development

When implementing CLI commands:
```bash
hugo-orchestrator namespace create --name "production"
hugo-orchestrator workflow start --type "OrderProcessing" --input "{...}"
hugo-orchestrator workflow signal --id "wf-123" --signal "UpdateOrder"
hugo-orchestrator workflow query --id "wf-123" --query "GetStatus"
```

## Documentation Requirements

For every new feature:
1. Update API documentation (OpenAPI/proto comments)
2. Add integration test examples
3. Update troubleshooting playbooks
4. Document migration impacts
5. Update performance baselines

## Code Review Checklist

- [ ] Workflows are deterministic
- [ ] Proper error handling with Result<T>
- [ ] Cancellation tokens propagated
- [ ] Metrics and traces emitted
- [ ] SQL queries use proper indexes
- [ ] Tests include replay validation
- [ ] Documentation updated
- [ ] Version gates for breaking changes

## Hugo-Specific Patterns

### Channel Usage
```csharp
var ch = Channel<WorkflowTask>(bufferSize: 100);
await Go(async () => {
    await foreach (var task in ch.Reader.ReadAllAsync(cancellationToken))
    {
        await ProcessTask(task);
    }
});
```

### WaitGroup Coordination
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

### ErrGroup Fan-out
```csharp
var eg = new ErrGroup();
foreach (var task in tasks)
{
    var t = task; // capture
    eg.Go(async () => await ProcessTask(t));
}
var results = await eg.Wait();
```

## Environment Variables

Required configuration:
```env
HUGO_ORCHESTRATOR_DB_CONNECTION=Server=localhost;Database=orchestrator;
HUGO_ORCHESTRATOR_ELASTICSEARCH_URL=http://localhost:9200
HUGO_ORCHESTRATOR_OTLP_ENDPOINT=http://localhost:4317
HUGO_ORCHESTRATOR_SHARD_COUNT=512
HUGO_ORCHESTRATOR_HISTORY_RETENTION_DAYS=30
```

## Support Resources

- Temporal Documentation: https://docs.temporal.io/
- Hugo Library Reference: [Internal docs] and https://github.com/df49b9cd/Hugo
- Architecture Decisions: docs/architecture/
- Runbooks: docs/operations/
- API Reference: docs/api/

Remember: The goal is to build a production-grade orchestration platform that leverages Hugo's elegant concurrency model while providing Temporal-like durability and operational excellence.
