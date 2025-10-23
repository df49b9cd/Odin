# Getting Started with Odin

This guide will help you get started with the Hugo Durable Orchestrator.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 22.12 LTS](https://nodejs.org/en/download) (for the control plane UI)
- [Docker](https://www.docker.com/get-started) and Docker Compose (for local development)
- PostgreSQL 14+ or MySQL 8.0.19+ (or use Docker Compose)

## Quick Start

### 1. Start Infrastructure

Using Docker Compose (recommended for local development):

```bash
docker-compose up -d postgres otel-collector jaeger prometheus grafana
```

Wait for services to be healthy:

```bash
docker-compose ps
```

### 2. Apply Database Schema

```bash
# TODO: Add schema migration command once implemented
# Example:
# dotnet run --project src/Odin.Cli -- migrate up
```

### 3. Start Odin Services

Option A: Using Docker Compose (full stack):

```bash
docker-compose up -d
```

Option B: Run locally for development:

```bash
# Terminal 1 - gRPC Service
cd src/Odin.ControlPlane.Grpc
dotnet run

# Terminal 2 - REST API
cd src/Odin.ControlPlane.Api
dotnet run

# Terminal 3 - System Workers
cd src/Odin.ExecutionEngine.SystemWorkers
dotnet run
```

### 4. Start the Control Plane UI

The UI is served by the REST API once built. During development you can run it with Vite for live reload:

```bash
cd src/Odin.ControlPlane.Ui
npm install
npm run dev
```

The dev server proxies API calls to `http://localhost:8080`. To produce a production build that the API can serve from `wwwroot`:

```bash
npm run build
```

This generates static assets under `src/Odin.ControlPlane.Api/wwwroot`. Restart the API after building to pick up fresh assets.

### 5. Verify Installation

Check service health:

```bash
# gRPC health check
grpcurl -plaintext localhost:7233 grpc.health.v1.Health/Check

# REST API health check
curl http://localhost:8080/health
```

Access monitoring dashboards:
- Grafana: http://localhost:3000 (admin/admin)
- Jaeger: http://localhost:16686
- Prometheus: http://localhost:9090

### 6. Create Your First Namespace

```bash
# Using CLI (TODO: implement)
# hugo-orchestrator namespace create --name "default"

# Or via REST API
curl -X POST http://localhost:8080/api/v1/namespaces \
  -H "Content-Type: application/json" \
  -d '{"name": "default", "description": "Default namespace"}'
```

### Optional: Run the UI via Docker Compose

To launch the Vite dev server inside Docker (useful if Node.js is not installed locally), enable the `ui` profile:

```bash
docker-compose --profile core --profile ui up odin-api odin-ui
```

The UI will be available at http://localhost:5173 and proxies API calls to the `odin-api` container.

## Creating Your First Workflow

### 1. Create a New Project

```bash
dotnet new console -n MyFirstWorkflow
cd MyFirstWorkflow
dotnet add package Odin.Sdk
```

### 2. Define a Workflow

```csharp
using static Hugo.Go;
using Odin.Sdk;

public class HelloWorldWorkflow : IWorkflow
{
    public async Task<Result<string>> ExecuteAsync(
        string name,
        CancellationToken cancellationToken)
    {
        // Execute an activity
        return await ExecuteActivity<GreetingActivity>(name)
            .Then(greeting => {
                Console.WriteLine(greeting);
                return Result.Ok(greeting);
            });
    }
}

public class GreetingActivity : IActivity<string, string>
{
    public async Task<Result<string>> ExecuteAsync(
    public Task<Result<string>> ExecuteAsync(
        string name,
        CancellationToken cancellationToken)
    {
        await Task.Delay(100); // Simulate work
        return Result.Ok($"Hello, {name}!");
        // Use Hugo's deterministic delay for testability
        await DelayAsync(TimeSpan.FromMilliseconds(100), cancellationToken);
        return Task.FromResult(Result.Ok($"Hello, {name}!"));
    }
}
```

### 3. Register Workers

```csharp
using Odin.Sdk;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddOdinWorker(options =>
        {
            options.GrpcEndpoint = "http://localhost:7233";
            options.Namespace = "default";
            options.TaskQueue = "hello-world";
        })
        .AddWorkflow<HelloWorldWorkflow>()
        .AddActivity<GreetingActivity>();
    })
    .Build();

await host.RunAsync();
```

### 4. Start a Workflow

```csharp
using Odin.Sdk;

var client = new OdinClient("localhost:7233");

var execution = await client.StartWorkflowAsync<HelloWorldWorkflow>(
    "World",
    new WorkflowOptions
    {
        Namespace = "default",
        TaskQueue = "hello-world",
        WorkflowId = "hello-workflow-1"
    });

Console.WriteLine($"Started workflow: {execution.WorkflowId}");

// Wait for result
var result = await execution.GetResultAsync();
Console.WriteLine($"Result: {result}");
```

## Next Steps

- **Explore the architecture**: [Architecture Overview](architecture/README.md)
- **Review the repository layout**: [Project Structure](PROJECT_STRUCTURE.md)
- **Track current progress**: [Phase 1 Progress](PHASE1_PROGRESS.md)
- **Explore samples**: Check out [samples/](../samples/)
- **Plan upcoming docs**: Workflow, activity, deployment, and operations guides are planned; see the doc index for status updates.

## Configuration

### Environment Variables

Create a `.env` file (see `.env.example`):

```bash
HUGO_ORCHESTRATOR_DB_CONNECTION=Server=localhost;Database=orchestrator;
HUGO_ORCHESTRATOR_ELASTICSEARCH_URL=http://localhost:9200
HUGO_ORCHESTRATOR_OTLP_ENDPOINT=http://localhost:4317
HUGO_ORCHESTRATOR_SHARD_COUNT=512
HUGO_ORCHESTRATOR_HISTORY_RETENTION_DAYS=30
```

### Connection Strings

**PostgreSQL:**
```
Server=localhost;Database=orchestrator;Username=odin;Password=your_password
```

**MySQL:**
```
Server=localhost;Database=orchestrator;User=odin;Password=your_password
```

## Troubleshooting

### Services won't start

Check Docker logs:
```bash
docker-compose logs odin-grpc
docker-compose logs odin-api
```

### Database connection issues

Verify PostgreSQL is running:
```bash
docker-compose ps postgres
```

Test connection:
```bash
psql -h localhost -U odin -d orchestrator
```

### gRPC connection errors

Ensure the gRPC service is listening:
```bash
netstat -an | grep 7233
```

Check firewall rules if running across networks.

## Getting Help

- Check the [documentation](../docs/)
- Review [architecture overview](architecture/README.md)
- See [common issues](operations/troubleshooting.md)
- Open an [issue](https://github.com/df49b9cd/Odin/issues)
