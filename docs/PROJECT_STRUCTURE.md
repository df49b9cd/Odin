# Odin Project Structure

This document describes the organization of the Odin codebase.

## Repository Layout

```
Odin/
├── .github/                        # GitHub workflows and automation guidance
│   ├── copilot-instructions.md
│   └── workflows/
│       └── ci.yml
├── deployment/                     # Deployment configurations and templates
│   ├── grafana/
│   ├── helm/
│   ├── kubernetes/
│   ├── terraform/
│   ├── otel-collector-config.yaml
│   └── prometheus.yml
├── docs/                           # Project documentation
│   ├── README.md                   # Documentation index
│   ├── PACKAGE_MANAGEMENT.md
│   ├── PHASE1_PROGRESS.md
│   ├── PROJECT_STRUCTURE.md
│   ├── TEST_FRAMEWORK_MIGRATION.md
│   ├── architecture/
│   │   └── README.md
│   ├── api/                        # Placeholder for detailed API docs
│   ├── operations/                 # Placeholder for operational runbooks
│   └── reference/
│       └── hugo-api-reference.md
├── samples/
│   └── OrderProcessing.Sample/
├── src/
│   ├── Odin.Cli/
│   ├── Odin.Contracts/
│   ├── Odin.ControlPlane.Api/
│   ├── Odin.ControlPlane.Grpc/
│   ├── Odin.Core/
│   ├── Odin.ExecutionEngine.History/
│   ├── Odin.ExecutionEngine.Matching/
│   ├── Odin.ExecutionEngine.SystemWorkers/
│   ├── Odin.Persistence/
│   ├── Odin.Sdk/
│   ├── Odin.Visibility/
│   └── Odin.WorkerHost/
├── tests/
│   ├── Odin.Core.Tests/
│   ├── Odin.ExecutionEngine.Tests/
│   ├── Odin.Integration.Tests/
│   └── Odin.Sdk.Tests/
├── .editorconfig
├── .env.example
├── .gitignore
├── CONTRIBUTING.md
├── Directory.Build.props
├── Directory.Packages.props
├── Dockerfile
├── PROJECT_README.md
├── README.md
├── SETUP_SUMMARY.md
├── Service Blueprint.md
├── docker-compose.yml
├── global.json
├── LICENSE
└── Odin.slnx
```

## Project Descriptions

### Source Projects

> The responsibilities listed below describe the intended end-state for each project. Unless explicitly marked as available today, the implementation is still evolving during Phase 1.

#### Odin.Contracts

**Type**: Class Library  
**Purpose**: Shared data transfer objects, interfaces, and contracts used across services  
**Key Files**:

- `WorkflowContracts.cs` - Workflow-related DTOs
- `ActivityContracts.cs` - Activity-related DTOs (TBD)
- `NamespaceContracts.cs` - Namespace management DTOs (TBD)

#### Odin.Core

**Type**: Class Library  
**Purpose**: Core utilities, extensions, and shared functionality  
**Key Components**:

- Hugo primitive extensions
- Result<T> extensions
- Common helpers and utilities
- Serialization utilities (TBD)

#### Odin.Persistence

**Type**: Class Library  
**Purpose**: Data access layer for SQL databases  
**Key Components**:

- Repository interfaces and implementations
- Database schema definitions
- Migration scripts
- Connection management
- Sharding logic

**Tables** (TBD):

- `namespaces`
- `workflow_executions`
- `history_events`
- `task_queues`
- `history_shards`
- `visibility_records`

#### Odin.ControlPlane.Api

**Type**: ASP.NET Core Web API  
**Purpose**: REST API facade for platform integrations  
**Port**: 8080 (HTTP), 8081 (HTTPS)  
**Key Features**:

- REST endpoints mirroring gRPC functionality
- OpenAPI/Swagger documentation
- Health checks
- Metrics endpoint

#### Odin.ControlPlane.Grpc

**Type**: ASP.NET Core gRPC Service  
**Purpose**: Temporal-compatible gRPC API  
**Port**: 7233  
**Key Features**:

- WorkflowService proto implementation
- Workflow lifecycle management
- Signal and query APIs
- Task queue polling
- Namespace CRUD

#### Odin.ExecutionEngine.History

**Type**: Class Library  
**Purpose**: History service for workflow state management  
**Key Components**:

- Sharded state management (512 shards default)
- Event history persistence
- Timer task queue
- Transfer task queue
- Replicator task queue
- Visibility task queue

#### Odin.ExecutionEngine.Matching

**Type**: Class Library  
**Purpose**: Task queue matching and distribution  
**Key Components**:

- Task queue partitioning
- Worker poll handling
- Task lease management
- Heartbeat tracking
- Built on `TaskQueueChannelAdapter<T>`

#### Odin.ExecutionEngine.SystemWorkers

**Type**: Worker Service  
**Purpose**: Internal system workflow orchestration  
**Current Focus**: Hosting infrastructure and background worker plumbing. Specific timers, retries, and archival workflows will be added during Phase 1 implementation.

#### Odin.Sdk

**Type**: Class Library  
**Purpose**: Worker SDK for workflow and activity development  
**Current Focus**: Public interfaces (`IWorkflow`, `IActivity`) and integration points for Hugo primitives. Deterministic execution helpers and replay utilities are planned.

#### Odin.WorkerHost

**Type**: Worker Service  
**Purpose**: Managed worker host for executing workflows and activities  
**Key Features**:

- Workflow execution engine
- Activity execution engine
- Task queue polling
- History replay
- Heartbeat management

#### Odin.Visibility

**Type**: Class Library  
**Purpose**: Workflow visibility and search  
**Key Components**:

- SQL advanced visibility
- Elasticsearch integration (optional)
- Visibility record persistence
- Query API
- Dual-write support for migrations

#### Odin.Cli

**Type**: Console Application  
**Purpose**: Command-line tool for Odin operations  
**Current Focus**: Project scaffolding and shared command abstractions. Command implementations will arrive alongside the control plane features.

### Test Projects

#### Odin.Core.Tests

**Type**: xUnit Test Project  
**Purpose**: Unit tests for core utilities

#### Odin.Sdk.Tests

**Type**: xUnit Test Project  
**Purpose**: Unit tests for SDK components

#### Odin.ExecutionEngine.Tests

**Type**: xUnit Test Project  
**Purpose**: Unit tests for execution engine components

#### Odin.Integration.Tests

**Type**: xUnit Test Project  
**Purpose**: End-to-end integration tests  
**Coverage**:

- Full workflow execution
- History replay
- Task queue distribution
- Visibility queries

### Sample Projects

#### OrderProcessing.Sample

**Type**: Console Application  
**Purpose**: Demonstrates order processing workflow  
**Features**:

- Workflow definition
- Activity implementations
- Worker registration
- Client usage

## Dependencies

### External Dependencies (NuGet)

- Hugo (1.0.0) - Concurrency primitives
- Hugo.Diagnostics.OpenTelemetry (1.0.0) - Observability
- Npgsql / MySql.Data - Database drivers
- Grpc.AspNetCore - gRPC support
- Elasticsearch.Net (optional) - Elasticsearch client
- Microsoft.Extensions.* - .NET hosting and DI

### Internal Dependencies

```
Odin.ControlPlane.Api → Odin.Contracts, Odin.Core, Odin.Persistence
Odin.ControlPlane.Grpc → Odin.Contracts, Odin.Core, Odin.ExecutionEngine.*
Odin.ExecutionEngine.* → Odin.Contracts, Odin.Core, Odin.Persistence
Odin.Sdk → Odin.Contracts, Odin.Core
Odin.WorkerHost → Odin.Sdk, Odin.Contracts
Odin.Cli → All
```

## Build Configuration

### Common Properties (Directory.Build.props)

- Target Framework: net10.0
- Nullable reference types enabled
- Implicit usings enabled
- XML documentation generation
- Code analysis enabled

### Solution Structure

```
Odin.sln
├── src (Solution Folder)
│   ├── Odin.Contracts
│   ├── Odin.Core
│   ├── Odin.Persistence
│   ├── Odin.ControlPlane.Api
│   ├── Odin.ControlPlane.Grpc
│   ├── Odin.ExecutionEngine.History
│   ├── Odin.ExecutionEngine.Matching
│   ├── Odin.ExecutionEngine.SystemWorkers
│   ├── Odin.Sdk
│   ├── Odin.WorkerHost
│   ├── Odin.Visibility
│   └── Odin.Cli
├── tests (Solution Folder)
│   ├── Odin.Core.Tests
│   ├── Odin.Sdk.Tests
│   ├── Odin.ExecutionEngine.Tests
│   └── Odin.Integration.Tests
└── samples (Solution Folder)
    └── OrderProcessing.Sample
```

## Development Workflow

1. **Clone and Setup**

   ```bash
   git clone https://github.com/df49b9cd/Odin.git
   cd Odin
   dotnet restore
   ```

2. **Build**

   ```bash
   dotnet build
   ```

3. **Test**

   ```bash
   dotnet test
   ```

4. **Run Locally**

   ```bash
   docker-compose up -d postgres
   dotnet run --project src/Odin.ControlPlane.Grpc
   ```

## Deployment

### Docker

```bash
docker build --target runtime-api -t odin-api:latest .
docker build --target runtime-grpc -t odin-grpc:latest .
docker build --target runtime-workers -t odin-workers:latest .
```

### Docker Compose

```bash
docker-compose up -d
```

### Kubernetes/Helm

```bash
helm install odin deployment/helm
```

## References

- [Architecture Documentation](docs/architecture/README.md)
- [Getting Started Guide](docs/getting-started.md)
- [Service Blueprint](Service%20Blueprint.md)
- [Copilot Instructions](.github/copilot-instructions.md)
