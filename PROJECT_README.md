# Odin - Hugo Durable Orchestrator

[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

A Temporal/Cadence-style workflow orchestration platform built on [Hugo 1.0.0](https://github.com/df49b9cd/Hugo) concurrency primitives. Odin provides durable workflow execution, history replay, and distributed task routing while maintaining Hugo as the core worker/runtime SDK.

## 🌟 Features

- **Durable Workflows**: Persist workflow state and replay from history
- **Hugo Integration**: Built on Hugo's elegant concurrency primitives (WaitGroup, ErrGroup, Channels, Result<T>)
- **Deterministic Execution**: Guarantees workflow replay consistency using DeterministicEffectStore and VersionGate
- **Task Distribution**: Efficient task queue routing with lease-aware delivery and heartbeats
- **Visibility**: Advanced workflow search via SQL or Elasticsearch
- **Observability**: Full OpenTelemetry integration with Prometheus, Jaeger, and Grafana
- **Multi-tenancy**: Namespace isolation with RBAC
- **Production Ready**: mTLS, monitoring, and operational tooling

## 🏗️ Architecture

### Core Components

1. **Control Plane**: Stateless API gateway (gRPC + REST) for workflow lifecycle management
2. **Execution Engine**: History service, matching service, and system workers
3. **Persistence Layer**: PostgreSQL/MySQL with optional Elasticsearch visibility
4. **Worker Runtime**: .NET 10 managed hosts using Hugo primitives
5. **Visibility System**: Workflow state tracking and search

### Technology Stack

- **Runtime**: .NET 10
- **Core Library**: Hugo 1.0.0
- **APIs**: gRPC (port 7233) + REST (port 8080)
- **Persistence**: PostgreSQL 14+ or MySQL 8.0.19+
- **Search**: Elasticsearch 8.x or SQL advanced visibility
- **Observability**: OpenTelemetry, Prometheus, Grafana, Jaeger

## 🚀 Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/get-started) and Docker Compose
- PostgreSQL 14+ (or use Docker Compose)

### Run with Docker Compose

```bash
# Clone the repository
git clone https://github.com/df49b9cd/Odin.git
cd Odin

# Start all services
docker-compose up -d

# Check service health
docker-compose ps

# View logs
docker-compose logs -f odin-grpc
```

Services will be available at:
- gRPC API: `localhost:7233`
- REST API: `localhost:8080`
- Grafana: `http://localhost:3000` (admin/admin)
- Jaeger UI: `http://localhost:16686`
- Prometheus: `http://localhost:9090`

### Build and Run Locally

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run the gRPC service
cd src/Odin.ControlPlane.Grpc
dotnet run

# In another terminal, run the REST API
cd src/Odin.ControlPlane.Api
dotnet run

# Run system workers
cd src/Odin.ExecutionEngine.SystemWorkers
dotnet run
```

### Run Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## 📖 Documentation

- [Architecture Overview](docs/architecture/README.md)
- [Getting Started Guide](docs/getting-started.md)
- [Workflow Development](docs/workflow-development.md)
- [Activity Implementation](docs/activity-implementation.md)
- [Deployment Guide](docs/deployment.md)
- [API Reference](docs/api/README.md)
- [Operations Manual](docs/operations/README.md)

## 🔧 Configuration

Odin is configured via environment variables:

```bash
# Database
HUGO_ORCHESTRATOR_DB_CONNECTION=Server=localhost;Database=orchestrator;

# Visibility
HUGO_ORCHESTRATOR_ELASTICSEARCH_URL=http://localhost:9200

# Telemetry
HUGO_ORCHESTRATOR_OTLP_ENDPOINT=http://localhost:4317

# Sharding
HUGO_ORCHESTRATOR_SHARD_COUNT=512

# Retention
HUGO_ORCHESTRATOR_HISTORY_RETENTION_DAYS=30
```

See [configuration documentation](docs/configuration.md) for all options.

## 💻 Usage Example

### Define a Workflow

```csharp
using static Hugo.Go;
using Odin.Sdk;

public class OrderProcessingWorkflow : IWorkflow
{
    public async Task<Result<OrderResult>> ExecuteAsync(
        OrderRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteActivity<ValidateOrderActivity>(request)
            .Then(validated => ExecuteActivity<ProcessPaymentActivity>(validated))
            .Then(paid => ExecuteActivity<FulfillOrderActivity>(paid))
            .Recover(error => ExecuteActivity<CompensateOrderActivity>(error))
            .Ensure(() => LogCompletion())
            .Finally(result => PersistAuditLog(result));
    }
}
```

### Start a Workflow

```bash
# Using CLI
hugo-orchestrator workflow start \
  --type "OrderProcessingWorkflow" \
  --namespace "production" \
  --task-queue "orders" \
  --input '{"orderId": "12345", "amount": 99.99}'

# Using SDK
var client = new OdinClient("localhost:7233");
var execution = await client.StartWorkflowAsync<OrderProcessingWorkflow>(
    new OrderRequest { OrderId = "12345", Amount = 99.99m },
    new WorkflowOptions
    {
        Namespace = "production",
        TaskQueue = "orders"
    });
```

## 🧪 Sample Applications

Check out the [samples](samples/) directory for complete examples:

- **OrderProcessing.Sample**: End-to-end order processing workflow
- More samples coming soon!

## 🤝 Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## 📋 Project Status

**Phase 1 (In Progress)**: Core control plane, execution engine, and basic SDK

- [x] Project structure
- [ ] Persistence layer implementation
- [ ] History service
- [ ] Matching service
- [ ] Worker SDK
- [ ] gRPC API implementation
- [ ] REST API facade
- [ ] CLI tool
- [ ] Integration tests

See [Service Blueprint](Service%20Blueprint.md) for the full roadmap.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built on [Hugo](https://github.com/df49b9cd/Hugo) concurrency primitives
- Inspired by [Temporal](https://temporal.io/) and [Cadence](https://cadenceworkflow.io/)
- Follows .NET and gRPC best practices

## 📞 Support

- GitHub Issues: [Create an issue](https://github.com/df49b9cd/Odin/issues)
- Documentation: [docs/](docs/)
- Community: Coming soon!

---

**Built with ❤️ using Hugo and .NET 10**
