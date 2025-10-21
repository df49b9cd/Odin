# Odin - Hugo Durable Orchestrator

[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![Build Status](https://github.com/df49b9cd/Odin/workflows/CI/badge.svg)](https://github.com/df49b9cd/Odin/actions)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

A Temporal/Cadence-style workflow orchestration platform built on [Hugo 1.0.0](https://github.com/df49b9cd/Hugo) concurrency primitives.

> **Status**: Phase 1 - Initial Development  
> The project structure is established. Core implementation is in progress.

## What is Odin?

Odin provides durable workflow execution with history replay and distributed task routing. It combines:

- **Hugo's elegant concurrency model** (WaitGroup, ErrGroup, Channels, Result<T>)
- **Temporal-style workflow orchestration** (durable execution, history replay, task queues)
- **Production-ready observability** (OpenTelemetry, Prometheus, Grafana)
- **.NET 10 modern runtime** with native performance

## Quick Links

- [Getting Started Guide](docs/getting-started.md)
- [Architecture Overview](docs/architecture/README.md)
- [Project Structure](docs/PROJECT_STRUCTURE.md)
- [Service Blueprint](Service%20Blueprint.md)
- [Contributing Guide](CONTRIBUTING.md)

## Key Features

✅ **Durable Workflows** - Workflows survive process restarts and replay from history  
✅ **Deterministic Execution** - Guaranteed replay consistency using Hugo primitives  
✅ **Task Distribution** - Efficient lease-aware task routing with heartbeats  
✅ **Advanced Visibility** - SQL or Elasticsearch-based workflow search  
✅ **Full Observability** - OpenTelemetry traces, Prometheus metrics, structured logs  
✅ **Multi-tenancy** - Namespace isolation with RBAC  
✅ **Production Ready** - mTLS, monitoring, operational tooling

## Project Structure

```
Odin/
├── src/                        # Source code
│   ├── Odin.Contracts/        # Shared DTOs and contracts
│   ├── Odin.Core/             # Core utilities
│   ├── Odin.Persistence/      # Data access layer
│   ├── Odin.ControlPlane.Api/ # REST API (port 8080)
│   ├── Odin.ControlPlane.Grpc/# gRPC service (port 7233)
│   ├── Odin.ExecutionEngine.*/# History, Matching, Workers
│   ├── Odin.Sdk/              # Worker SDK
│   ├── Odin.WorkerHost/       # Worker runtime
│   ├── Odin.Visibility/       # Visibility service
│   └── Odin.Cli/              # CLI tool
├── tests/                      # Test projects
├── samples/                    # Example workflows
├── docs/                       # Documentation
└── deployment/                 # Docker, K8s, Helm, Terraform
```

See [Project Structure](docs/PROJECT_STRUCTURE.md) for details.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/get-started) and Docker Compose
- PostgreSQL 14+ (or use Docker Compose)

### Run with Docker Compose

```bash
git clone https://github.com/df49b9cd/Odin.git
cd Odin
docker-compose up -d
```

Access:
- gRPC: `localhost:7233`
- REST API: `localhost:8080`
- Grafana: http://localhost:3000 (admin/admin)
- Jaeger: http://localhost:16686

### Build and Test

```bash
dotnet restore
dotnet build
dotnet test
```

## Development Status

**Phase 1 (In Progress)**:
- [x] Project structure and solution setup
- [x] Docker and deployment configurations
- [x] Documentation framework
- [x] Basic project interfaces
- [ ] Persistence layer implementation
- [ ] History service
- [ ] Matching service
- [ ] Worker SDK with Hugo integration
- [ ] gRPC API implementation
- [ ] CLI tool

See [Service Blueprint](Service%20Blueprint.md) for the full roadmap.

## Technology Stack

- **Runtime**: .NET 10
- **Core Library**: Hugo 1.0.0 (concurrency primitives)
- **APIs**: gRPC (Temporal-compatible) + REST
- **Persistence**: PostgreSQL 14+ or MySQL 8.0.19+
- **Visibility**: Elasticsearch 8.x or SQL advanced visibility
- **Observability**: OpenTelemetry, Prometheus, Grafana, Jaeger
- **Deployment**: Docker, Kubernetes, Helm

## Example Workflow

```csharp
using Odin.Sdk;

public class OrderWorkflow : IWorkflow<OrderRequest, OrderResult>
{
    public async Task<Result<OrderResult>> ExecuteAsync(
        OrderRequest input,
        CancellationToken cancellationToken)
    {
        return await ExecuteActivity<ValidateOrderActivity>(input)
            .Then(validated => ExecuteActivity<ProcessPaymentActivity>(validated))
            .Then(paid => ExecuteActivity<FulfillOrderActivity>(paid))
            .Recover(error => ExecuteActivity<CompensateOrderActivity>(error))
            .Finally(result => PersistAuditLog(result));
    }
}
```

See [samples/](samples/) for complete examples.

## Documentation

- **Getting Started**: [docs/getting-started.md](docs/getting-started.md)
- **Architecture**: [docs/architecture/README.md](docs/architecture/README.md)
- **Workflow Development**: Coming soon
- **API Reference**: Coming soon
- **Operations**: Coming soon

## Contributing

We welcome contributions! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

This project is licensed under the MIT License - see [LICENSE](LICENSE) file.

## Acknowledgments

- Built on [Hugo](https://github.com/df49b9cd/Hugo) concurrency primitives
- Inspired by [Temporal](https://temporal.io/) and [Cadence](https://cadenceworkflow.io/)

## Support

- GitHub Issues: [Create an issue](https://github.com/df49b9cd/Odin/issues)
- Documentation: [docs/](docs/)

---

**Built with Hugo and .NET 10**
