# Odin Project Structure

This document describes the organization of the Odin codebase.

## Repository Layout

```
Odin/
├── .github/                      # GitHub-specific files
│   ├── copilot-instructions.md  # AI coding assistant instructions
│   └── workflows/               # CI/CD workflows
│       └── ci.yml              # Main CI pipeline
├── deployment/                  # Deployment configurations
│   ├── grafana/                # Grafana dashboards and datasources
│   ├── helm/                   # Helm chart for Kubernetes
│   ├── kubernetes/             # Raw Kubernetes manifests
│   ├── terraform/              # Infrastructure as code
│   ├── otel-collector-config.yaml  # OpenTelemetry configuration
│   └── prometheus.yml          # Prometheus scrape config
├── docs/                       # Documentation
│   ├── api/                    # API reference documentation
│   ├── architecture/           # Architecture documentation
│   │   └── README.md          # Architecture overview
│   ├── operations/             # Operations and runbooks
│   ├── getting-started.md      # Getting started guide
│   ├── workflow-development.md # Workflow authoring guide
│   └── deployment.md           # Deployment guide
├── samples/                    # Sample applications
│   └── OrderProcessing.Sample/ # Order processing workflow example
├── src/                        # Source code
│   ├── Odin.Contracts/        # Shared contracts and DTOs
│   ├── Odin.Core/             # Core utilities and extensions
│   ├── Odin.Persistence/      # Data access layer
│   ├── Odin.ControlPlane.Api/ # REST API service
│   ├── Odin.ControlPlane.Grpc/# gRPC service (Temporal-compatible)
│   ├── Odin.ExecutionEngine.History/     # History service
│   ├── Odin.ExecutionEngine.Matching/    # Matching service
│   ├── Odin.ExecutionEngine.SystemWorkers/ # System workers
│   ├── Odin.Sdk/              # Worker SDK
│   ├── Odin.WorkerHost/       # Worker host service
│   ├── Odin.Visibility/       # Visibility service
│   └── Odin.Cli/              # Command-line tool
├── tests/                      # Test projects
│   ├── Odin.Core.Tests/       # Core library tests
│   ├── Odin.Sdk.Tests/        # SDK tests
│   ├── Odin.ExecutionEngine.Tests/ # Execution engine tests
│   └── Odin.Integration.Tests/    # End-to-end tests
├── .editorconfig               # Code style configuration
├── .env.example                # Example environment variables
├── .gitignore                  # Git ignore patterns
├── CONTRIBUTING.md             # Contribution guidelines
├── Directory.Build.props       # MSBuild common properties
├── docker-compose.yml          # Docker Compose configuration
├── Dockerfile                  # Multi-stage Dockerfile
├── global.json                 # .NET SDK version
├── LICENSE                     # MIT License
├── Odin.sln                    # Solution file
├── PROJECT_README.md           # Project README
├── README.md                   # Main README
└── Service Blueprint.md        # Service design document
```

## Project Descriptions

### Source Projects

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
**Key Workers**:
- Timer worker
- Retry worker
- Cleanup worker
- History archival worker
- Uses Hugo's `WaitGroup` + `ErrGroup`

#### Odin.Sdk
**Type**: Class Library  
**Purpose**: Worker SDK for workflow and activity development  
**Key Components**:
- `IWorkflow<TInput, TOutput>` interface
- `IActivity<TInput, TOutput>` interface
- Workflow execution context
- Activity heartbeat support
- DeterministicEffectStore integration
- VersionGate for workflow versioning
- Hugo primitive wrappers

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
**Commands** (TBD):
```bash
hugo-orchestrator namespace create --name "production"
hugo-orchestrator workflow start --type "OrderProcessing" --input "{...}"
hugo-orchestrator workflow signal --id "wf-123" --signal "UpdateOrder"
hugo-orchestrator workflow query --id "wf-123" --query "GetStatus"
hugo-orchestrator migrate up
```

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
