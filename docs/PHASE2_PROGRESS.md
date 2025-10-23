# Phase 2 Readiness Plan

**Date**: October 23, 2025  
**Status**: In Progress – Kickoff Underway  
**Reporting Cadence**: Weekly updates every Friday, rolling 4-week snapshot

## Phase 2 Objectives

1. **Cadence-style web UI (highest priority)** – deliver a control plane UI for monitoring workflows, task queues, workers, and namespaces with live updates and troubleshooting tools.  
2. **Operational multi-worker platform** – run multiple worker hosts concurrently, managed and observed through the control plane (API + UI), with stateful task routing and scaling guidance.  
3. **Extended persistence matrix** – add Microsoft SQL Server and Azure Cosmos DB (or equivalent distributed document store) support alongside PostgreSQL, with migration automation, compatibility tests, and durability tooling.  
4. **Observability & operational excellence** – instrument the platform with tracing, metrics, logs, and health checks suitable for 24/7 operations.  
5. **Advanced workflow features & CI/CD** – close API gaps with Temporal v1, enhance developer tooling, and automate build/test/release pipelines.

## Key Milestones

| Milestone | Target | Description |
|-----------|--------|-------------|
| M1 – Control plane web UI alpha | Week 2 | Ship initial React/Blazor SPA with workflow list, execution detail, task queue view, and worker health dashboards backed by control plane APIs. |
| M2 – Multi-worker operations baseline | Week 3 | Deploy multiple worker hosts with sticky routing, capture UI-driven controls (pause, drain), and document scaling & failover playbooks. |
| M3 – Observability stack online | Week 4 | Integrate OpenTelemetry tracing, Prometheus/Grafana dashboards, structured logging, and alertable health probes surfaced in the UI. |
| M4 – Cross-database persistence (PostgreSQL + SQL Server + Cosmos DB) | Week 5 | Deliver SQL Server schema/migrations, Cosmos DB data model, automated compatibility tests, and migration verification pipelines. |
| M5 – Advanced workflow APIs & CI/CD | Week 6 | Ship schedule/retry enhancements, container images, GitHub Actions pipelines, smoke tests, and release documentation integrated with the UI. |

> **Note**: Milestones build on each other; delays should be communicated via the weekly report and reflected in the tracking table below.

## Kickoff Update (October 23, 2025)

- Weekly execution cadence confirmed; first Friday status review scheduled for October 24.  
- Milestone and workstream owners identified across Observability, Control Plane UI, Persistence, Execution/Workers, and DevEx.  
- Phase 2 GitHub Projects board seeded with initial M1 issues (OTEL control plane instrumentation, UI scaffold, multi-worker sandbox environment).  
- Telemetry baseline checklist drafted to capture pre-instrumentation metrics for comparison once OTEL is enabled.

## Week 1 Focus (Oct 23 – Oct 31, 2025)

- Observability: instrument control plane API + worker hosts with OTEL tracing/metrics prototype, capture baseline dashboards.  
- Control Plane UI: finalize React + TypeScript stack decision and scaffold repository with routing/layout, start workflow list wireframes.  
- Control Plane & Deployment: stand up Kubernetes sandbox with ≥3 worker hosts, validate sticky routing and capture initial operational metrics.  
- Persistence: outline SQL Server migration strategy (schema gaps, stored procedures) and Cosmos DB data modeling approach for workflow/visibility entities.  
- Developer Experience & CI/CD: draft GitHub Actions workflow skeleton (build, unit, lint) and document environment bootstrap steps for contributors.

## Workstreams & Deliverables

### 1. Observability & Reliability

- [ ] OpenTelemetry tracing for API, gRPC, persistence, and workflow execution paths.  
- [ ] Prometheus metrics (workflow throughput, queue depth, shard ownership, worker heartbeats).  
- [ ] Structured logging with correlation IDs, namespace + workflow labels.  
- [ ] Synthetic health checks and readiness probes.  
- [ ] Pager playbook (alert thresholds, runbooks for common incidents).  
- [ ] UI dashboards for live telemetry, alert status, and incident response shortcuts.  
- [ ] Service-level objectives (SLOs) defined for latency, error rate, and availability, enforced through monitoring.

### 2. Control Plane Web UI

- [ ] UX research & wireframes mirroring Cadence UI primitives (workflow lists, filters, execution detail timelines).  
- [ ] Front-end stack selection (React + TypeScript + Vite, or Blazor WASM) with component library.  
- [ ] Real-time updates (SignalR/WebSockets or polling) for workflow state, task queues, and workers.  
- [ ] Drill-down views: execution history, task queue depth, worker lease stats, namespace settings.  
- [ ] Operational actions (terminate, signal, reset, drain queue) with RBAC checks.  
- [ ] Authentication/authorization integration (OIDC/OpenID Connect) and audit logging.

### 3. Control Plane & Deployment

- [ ] Helm charts / Terraform modules for multi-instance control plane and worker host.  
- [ ] Session affinity and sticky task queue routing validation in Kubernetes.  
- [ ] Rolling upgrade playbooks (zero-downtime control plane, schema migration guidance).  
- [ ] Automated migration gating (pre-flight checks, backup hooks).  
- [ ] Deployment automation for UI + API + worker hosts with shared configuration.

### 4. Persistence & Data Durability

- [ ] SQL Server 2022 migration set mirroring PostgreSQL schemas (stored procedures, concurrency patterns).  
- [ ] Cosmos DB (Core/SQL API) data model for namespaces, workflow executions, history, task queues, and visibility records.  
- [ ] Repository integration tests across PostgreSQL, SQL Server, and Cosmos DB via Testcontainers/Azure Cosmos emulator.  
- [ ] Cross-provider abstraction compliance (feature flags, connection string handling, consistency levels).  
- [ ] Backup/restore & PITR guides per provider; data retention & archival policies.

### 5. Execution Engine & Workers

- [ ] Sticky workflow execution support and advancement of task queue partitioning.  
- [ ] WorkerHost autoscaling validation under synthetic load (Hugo TaskQueue metrics).  
- [ ] Lease contention and dead-letter queue strategy.  
- [ ] Replay/regression suite (deterministic effect store validation, version gate scenarios).  
- [ ] Cross-language worker interoperability spike (C# + TypeScript example).  
- [ ] Control plane + UI observability of worker heartbeats, leases, and backlog with multi-worker controls.

### 6. Developer Experience & CI/CD

- [ ] GitHub Actions (or Azure DevOps) pipelines covering build, unit, integration, lint, and container publication.  
- [ ] Smoke test suite triggered post-deployment (worker host end-to-end scenario).  
- [ ] Developer onboarding guide (scaffold environment, run tests, seed sample workloads).  
- [ ] API & SDK documentation refresh (include new Phase 2 features).  
- [ ] Release notes & semantic versioning policy.

## Tracking & Metrics

| Metric | Target | Tracking Notes |
|--------|--------|----------------|
| Observability SLO compliance | ≥ 99% adherence to latency & availability SLOs with <1% missing telemetry | Monitored via Prometheus/Grafana dashboards fed by OTEL; enforced through alerting. |
| UI adoption | ≥ 90% parity vs. CLI for core operational actions | Telemetry: unique operators using UI, task actions triggered via UI. |
| Multi-worker soak test | 48h continuous run with ≥ 3 worker hosts | Monitor task throughput, CPU/memory, failover events. |
| Workflow latency p95 | < 500 ms task dispatch → worker completion | Collected via OTEL traces and Prometheus (per DB provider). |
| Test coverage | ≥ 85% critical services (Control Plane, UI, Execution Engine, Persistence) | Enforced via CI coverage thresholds. |
| Migration success rate | 100% green across PostgreSQL, SQL Server, Cosmos DB in CI | Validate using Testcontainers/Cosmos emulator in pipeline. |

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| UI scope creep / UX complexity | Delayed delivery of operational tooling | Incremental releases (alpha → beta), focus on parity features first, solicit operator feedback weekly. |
| Multi-database drift (PostgreSQL vs. SQL Server vs. Cosmos DB) | Schema divergence, inconsistent behaviour | Introduce contract tests, migration linting, automated schema diff checks per provider. |
| Observability overhead | Performance regression from instrumentation | Feature flag sampling, benchmark before enabling globally. |
| Deterministic replay regressions | Workflow failures during upgrades | Maintain replay suite, require passing before release. |
| Operational complexity | Difficult rollout to production | Provide automation scripts, detailed runbooks, and training sessions. |
| Cosmos DB consistency / throughput limits | Task visibility lag, higher costs | Configure appropriate consistency levels, partition keys, and autoscale; monitor RU consumption in tests. |

## Dependencies

- .NET 10 GA availability (monitor RC updates).  
- Hugo library updates (watch for 1.x patch releases).  
- React/TypeScript (or Blazor) UI stack, component library selection.  
- Docker / Kubernetes test infrastructure for multi-instance validation.  
- SQL Server 2022 containers, Cosmos DB emulator/Azure subscription access.  
- Availability of Elasticsearch (optional for enhanced visibility search).

## Next Steps

- [x] Approve revised Phase 2 goals prioritising observability foundations alongside the web UI and multi-worker operations.  
- [ ] Break down observability, UI, and persistence expansion workstreams into GitHub issues/epics, assign owners.  
- [ ] Stand up shared dashboards (Jira/Boards) linking telemetry coverage, UI adoption metrics, database compatibility progress, and worker soak tests.  
- [ ] Kick off M1 tasks: instrument core services with OTEL, wireframe the Cadence-style UI, scaffold the front-end project, and align infra team on multi-worker sandbox requirements.
