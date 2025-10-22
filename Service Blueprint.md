# Service Blueprint

## Overview

- Launch the “Hugo Durable Orchestrator” as a Temporal/Cadence-style control plane that layers Hugo 1.0.0 concurrency primitives and the `Hugo.Diagnostics.OpenTelemetry` 1.0.0 instrumentation stack to deliver durable workflow execution, history replay, and task routing while keeping Hugo as the worker/runtime SDK.
- Constrain the initial release to single-region, single-cluster deployments with pluggable SQL persistence (PostgreSQL 14+/MySQL 8.0.19+) and optional Elasticsearch 8.x or SQL advanced visibility per Temporal v1.20+ guidance from [Temporal service docs](https://docs.temporal.io/temporal-service).
- Provide managed worker and site runtimes that consume orchestrator task queues through Hugo’s primitives (`WaitGroup`, `ErrGroup`, `TaskQueueChannelAdapter<T>`, channels) so customers can adopt either the raw Hugo library or the orchestrator SDK with consistent cancellation semantics.
- Expose a hosted API surface (gRPC + REST) for namespaces, workflow lifecycle, signals, queries, schedules, and visibility that mirrors Temporal Frontend behaviour ([Temporal server architecture](https://docs.temporal.io/temporal-service/temporal-server)) while enforcing deterministic workflow definitions.

## Platform Architecture

### Control Plane

- Stateless front door that terminates mutual TLS, enforces rate limits, authenticates workloads, and brokers namespace CRUD, signal/query APIs, and workflow lifecycle calls.
- gRPC surface adopts the Temporal WorkflowService proto (port 7233) while a REST façade maps to the same operations for platform integrations; request routing hashes workflow identifiers to history shards.
- Integrate OpenTelemetry tracing via `AddHugoDiagnostics` so every inbound request emits `workflow.*` attributes and shares schema URLs with worker telemetry.

### Execution Engine

- **History Service:** Sharded mutable state and event history processors; configure shard count up front, backed by SQL persistence. Implements timer, transfer, replicator, and visibility task queues consistent with Temporal documentation.
- **Matching Service:** Partitioned task queues and poll dispatch powered by Hugo’s `TaskQueue<T>` and `TaskQueueChannelAdapter<T>` for lease-aware delivery, heartbeats, and retry semantics.
- **System Workers:** Internal workers orchestrated with `WaitGroup` + `ErrGroup` run timers, retries, and cleanup workflows, emitting deterministic state via `DeterministicEffectStore` during replay.
- **Workflow Replay:** Rehydrate workflow state using `WorkflowExecutionContext` and `VersionGate` to guarantee deterministic replays when workers reschedule tasks.

### Persistence Layer

- Primary SQL backing store (PostgreSQL/MySQL) for namespaces, workflow mutable state, event history, task queues, and shard metadata per [Temporal persistence guidance](https://docs.temporal.io/temporal-service/persistence).
- Optional Elasticsearch/OpenSearch cluster or SQL advanced visibility (Temporal v1.20+) for indexed search; support dual visibility migrations (Temporal v1.21) and retention policies using TTL or scheduled cleanup.
- Provide schema migrations and liquibase/flyway automation plus health probes that validate shard ownership, history cleanup latency, and task queue backlogs.

### Worker Runtime & SDK

- Managed .NET 10 worker host built on Hugo primitives: `WaitGroup` for lifecycle coordination, `ErrGroup` for cancellable fan-out, channels for state propagation, and `TaskQueueChannelAdapter<T>` for lease requeue and fault recovery.
- Workflows and activities rely on `Result<T>` pipelines, `DeterministicEffectStore`, and `VersionGate` to capture deterministic side effects and versioned branching.
- Package background services for workflow execution, history replay, activity heartbeats, and CLI-driven task queue consumers. Provide templates demonstrating hosted service integration and Aspire distributed app support.
- Ship `AddHugoDiagnostics` defaults for OTLP gRPC export, optional Prometheus scraping, rate-limited span sampling, and schema-aligned workflow metrics.

### Visibility & Operations

- Persist `WorkflowVisibilityRecord` snapshots produced by `WorkflowExecutionContext` with canonical columns (`namespace`, `workflow_id`, `run_id`, `status`, `task_queue`, `logical_clock`, `replay_count`, `attributes`).
- Offer SQL and Elasticsearch query surfaces that align with the documented playbook (docs/how-to/workflow-visibility.md) for active workflow aging, replay spikes, and failure analysis.
- Emit metrics and traces (`workflow.duration`, `workflow.replay.count`, queue latency histograms) through Hugo diagnostics; surface dashboards via Aspire or Grafana using OTLP/Prometheus exporters.

### Security & Multitenancy

- Namespace-level isolation, role-based access control, and policy enforcement similar to Temporal’s namespace model; integrate with identity providers for token issuance.
- Enforce mTLS between control-plane services, workers, and persistence tiers; rotate certificates via platform secret stores.
- Adopt Temporal worker identity recommendations ([worker identity guidance](https://docs.temporal.io/workers)) by deriving worker IDs from deployment context (cluster/region/pod) and surfacing them in visibility and metrics.

## Hugo Integration Principles

- Standardize on `using static Hugo.Go;` imports so orchestrator code and SDK samples mirror existing Hugo usage.
- Reuse channels, wait groups, err groups, and deferred cleanup for polling loops, heartbeat pipelines, and system workflow orchestration to ensure deterministic cancellation semantics.
- Build workflow state transitions and retries with `Result<T>` pipelines (`Then`, `Recover`, `Ensure`, `Finally`) instead of manual branching.
- Use `DeterministicEffectStore` + `VersionGate` for workflow change management and replay safety; gate incompatible changes through version markers persisted alongside history.
- Propagate ambient metadata (`workflow.*`) from API ingress through workers using `WorkflowExecutionContext` and `GoDiagnostics` so logs, traces, and visibility stores stay aligned.

## API Surface

- gRPC WorkflowService (Temporal proto) for namespaces, workflow lifecycle, task queue polling, signals, queries, batch operations, and schedules.
- REST translation for platform customers that mirrors gRPC semantics, provides pagination/filters for visibility, and surfaces OpenAPI definitions.
- CLI tooling that leverages the same APIs for namespace management, workflow introspection, history replay, and schedule administration.

## Persistence & Visibility Stores

- Define SQL schemas for history shards, workflow execution state, task queues, and visibility records; include shard and task queue partitions sized to match anticipated load (start with 512 history shards per Temporal guidance).
- Enable advanced visibility either through SQL (PostgreSQL 12+/MySQL 8.0.17+) or Elasticsearch; implement dual-write strategy to support migrations per Temporal v1.21 dual visibility guidance.
- Configure retention policies so namespaces set retention days on registration; schedule cleanup workers to delete histories and visibility rows when timers fire.

## Incremental Delivery Plan

- **Phase 0 (4 weeks):** Finalize requirements, ERDs, and persistence schemas; prototype SQL history shards, task queue tables, and namespace admin flows; validate Hugo integration needs (TaskQueueChannelAdapter usage, deterministic state store persistence) and capture any required library extensions.
- **Phase 1 (8–10 weeks, in progress):** Finish PostgreSQL-backed persistence (complete Dapper repositories + integration coverage), stand up the Result-oriented worker SDK core (ResultExtensions, workflow/activity abstractions, DeterministicEffectStore), scaffold history/matching services with Hugo TaskQueue plumbing, expose an initial gRPC WorkflowService façade, and ship CLI-driven smoke tests across in-memory and PostgreSQL providers.
- **Phase 2 (6–8 weeks):** Add visibility APIs, Elasticsearch/SQL advanced search, workflow archiving, retention management, and workflow versioning; publish dashboards using Aspire/OpenTelemetry exporters and document troubleshooting playbooks.
- **Phase 3 (8–10 weeks):** Introduce namespace auth/multitenancy, worker identity policies, cross-region replication design spike, blue/green deployment tooling, production hardening (observability SLOs, load tests, upgrade playbooks), and compliance reporting.

## Operational Considerations

- Create deterministic integration tests that replay event histories against new worker builds using `WorkflowExecutionContext` utilities before deployment.
- Provide Helm charts and Terraform modules for orchestrator deployment, including persistence, optional visibility store, certificate automation, and telemetry plumbing.
- Document migration paths and developer UX: when to consume Hugo standalone versus the orchestrator SDK, including upgrade checklists for workflow determinism.
- Maintain playbooks for timeouts, retries, compensation, and cross-runtime samples; keep docs synchronized with shipped APIs and diagnostics defaults.
