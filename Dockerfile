# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY Odin.slnx ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY src/Odin.Contracts/*.csproj ./src/Odin.Contracts/
COPY src/Odin.Core/*.csproj ./src/Odin.Core/
COPY src/Odin.Persistence/*.csproj ./src/Odin.Persistence/
COPY src/Odin.ControlPlane.Api/*.csproj ./src/Odin.ControlPlane.Api/
COPY src/Odin.ControlPlane.Grpc/*.csproj ./src/Odin.ControlPlane.Grpc/
COPY src/Odin.ExecutionEngine.History/*.csproj ./src/Odin.ExecutionEngine.History/
COPY src/Odin.ExecutionEngine.Matching/*.csproj ./src/Odin.ExecutionEngine.Matching/
COPY src/Odin.ExecutionEngine.SystemWorkers/*.csproj ./src/Odin.ExecutionEngine.SystemWorkers/
COPY src/Odin.Sdk/*.csproj ./src/Odin.Sdk/
COPY src/Odin.WorkerHost/*.csproj ./src/Odin.WorkerHost/
COPY src/Odin.Visibility/*.csproj ./src/Odin.Visibility/
COPY src/Odin.Cli/*.csproj ./src/Odin.Cli/
COPY samples/OrderProcessing.Sample/*.csproj ./samples/OrderProcessing.Sample/
COPY samples/OrderProcessing.Shared/*.csproj ./samples/OrderProcessing.Shared/
COPY tests/Odin.ControlPlane.Grpc.Tests/*.csproj ./tests/Odin.ControlPlane.Grpc.Tests/
COPY tests/Odin.Core.Tests/*.csproj ./tests/Odin.Core.Tests/
COPY tests/Odin.ExecutionEngine.Tests/*.csproj ./tests/Odin.ExecutionEngine.Tests/
COPY tests/Odin.Persistence.Tests/*.csproj ./tests/Odin.Persistence.Tests/
COPY tests/Odin.Integration.Tests/*.csproj ./tests/Odin.Integration.Tests/
COPY tests/Odin.Sdk.Tests/*.csproj ./tests/Odin.Sdk.Tests/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY . .

# Build solution
RUN dotnet build -c Release --no-restore

# Publish stage - Control Plane API
FROM build AS publish-api
RUN dotnet publish src/Odin.ControlPlane.Api/Odin.ControlPlane.Api.csproj -c Release -o /app/publish/api --no-build

# Publish stage - gRPC Service
FROM build AS publish-grpc
RUN dotnet publish src/Odin.ControlPlane.Grpc/Odin.ControlPlane.Grpc.csproj -c Release -o /app/publish/grpc --no-build

# Publish stage - System Workers
FROM build AS publish-workers
RUN dotnet publish src/Odin.ExecutionEngine.SystemWorkers/Odin.ExecutionEngine.SystemWorkers.csproj -c Release -o /app/publish/workers --no-build

# Publish stage - Worker Host
FROM build AS publish-workerhost
RUN dotnet publish src/Odin.WorkerHost/Odin.WorkerHost.csproj -c Release -o /app/publish/workerhost --no-build

# Publish stage - CLI
FROM build AS publish-cli
RUN dotnet publish src/Odin.Cli/Odin.Cli.csproj -c Release -o /app/publish/cli --no-build

# Runtime stage - Control Plane API
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-api
WORKDIR /app
COPY --from=publish-api /app/publish/api .
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD curl -f http://localhost:8080/ || exit 1
EXPOSE 8080
EXPOSE 8081
ENTRYPOINT ["dotnet", "Odin.ControlPlane.Api.dll"]

# Runtime stage - gRPC Service
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-grpc
WORKDIR /app
COPY --from=publish-grpc /app/publish/grpc .
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD curl -f http://localhost:7233/ || exit 1
EXPOSE 7233
ENTRYPOINT ["dotnet", "Odin.ControlPlane.Grpc.dll"]

# Runtime stage - System Workers
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-workers
WORKDIR /app
COPY --from=publish-workers /app/publish/workers .
ENTRYPOINT ["dotnet", "Odin.ExecutionEngine.SystemWorkers.dll"]

# Runtime stage - Worker Host
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-workerhost
WORKDIR /app
COPY --from=publish-workerhost /app/publish/workerhost .
ENTRYPOINT ["dotnet", "Odin.WorkerHost.dll"]

# Runtime stage - CLI
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime-cli
WORKDIR /app
COPY --from=publish-cli /app/publish/cli .
ENTRYPOINT ["dotnet", "Odin.Cli.dll"]
