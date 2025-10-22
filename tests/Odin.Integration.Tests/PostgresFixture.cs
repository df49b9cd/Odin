using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dapper;
using Docker.DotNet;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Odin.Persistence;
using Odin.Persistence.Repositories;
using Testcontainers.PostgreSql;
using Xunit;
using DotNet.Testcontainers.Configurations;

namespace Odin.Integration.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    static PostgresFixture()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private static readonly string[] RequiredMigrations =
    {
        "001_namespaces.up.sql",
        "002_history_shards.up.sql",
        "003_workflow_executions.up.sql"
    };

    private readonly PostgreSqlContainer _container;
    private bool _dockerAvailable;
    private string? _skipReason;

    public PostgresFixture()
    {
        var builder = new PostgreSqlBuilder()
            .WithDatabase("odin")
            .WithUsername("postgres")
            .WithPassword("postgres");

        builder = ConfigureContainerRuntime(builder);

        _container = builder.Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
            await ApplyMigrationsAsync();
            await EnsureShardsInitializedAsync();
            _dockerAvailable = true;
        }
        catch (Exception ex) when (IsDockerUnavailable(ex))
        {
            _skipReason = $"Docker is required for PostgreSQL integration tests: {ex.Message}";
        }
    }

    public ValueTask DisposeAsync()
        => _container.DisposeAsync();

    public async Task ResetDatabaseAsync()
    {
        EnsureDockerIsRunning();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync("TRUNCATE TABLE workflow_executions CASCADE;");
        await connection.ExecuteAsync("TRUNCATE TABLE namespaces CASCADE;");
        await connection.ExecuteAsync("TRUNCATE TABLE history_shards CASCADE;");

        await EnsureShardsInitializedAsync();
    }

    public async Task<Guid> CreateNamespaceAsync(string namespaceName)
    {
        EnsureDockerIsRunning();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = @"
INSERT INTO namespaces (namespace_id, namespace_name)
VALUES (@NamespaceId, @NamespaceName)
RETURNING namespace_id;
";

        var namespaceId = Guid.NewGuid();
        return await connection.ExecuteScalarAsync<Guid>(sql, new
        {
            NamespaceId = namespaceId,
            NamespaceName = namespaceName
        });
    }

    public PostgreSqlConnectionFactory CreateConnectionFactory()
    {
        return new PostgreSqlConnectionFactory(
            ConnectionString,
            NullLogger<PostgreSqlConnectionFactory>.Instance);
    }

    public WorkflowExecutionRepository CreateWorkflowExecutionRepository()
    {
        return new WorkflowExecutionRepository(
            CreateConnectionFactory(),
            NullLogger<WorkflowExecutionRepository>.Instance);
    }

    public ShardRepository CreateShardRepository()
    {
        return new ShardRepository(
            CreateConnectionFactory(),
            NullLogger<ShardRepository>.Instance);
    }

    public void EnsureDockerIsRunning()
    {
        if (_dockerAvailable)
        {
            return;
        }

        Assert.Skip(_skipReason ?? "Docker is required for PostgreSQL integration tests.");
    }

    private async Task ApplyMigrationsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS pgcrypto;");

        foreach (var script in await LoadMigrationScriptsAsync())
        {
            await connection.ExecuteAsync(script);
        }
    }

    private static async Task<IReadOnlyList<string>> LoadMigrationScriptsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var migrationsPath = Path.Combine(root, "src", "Odin.Persistence", "Migrations", "PostgreSQL");

        var scripts = new List<string>(RequiredMigrations.Length);
        foreach (var file in RequiredMigrations)
        {
            var absolutePath = Path.Combine(migrationsPath, file);
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException($"Migration file '{file}' was not found at '{absolutePath}'.");
            }

            var sql = await File.ReadAllTextAsync(absolutePath);
            scripts.Add(sql);
        }

        return scripts;
    }

    private async Task EnsureShardsInitializedAsync()
    {
        var shardRepository = CreateShardRepository();
        var initializeResult = await shardRepository.InitializeShardsAsync(512);
        if (initializeResult.IsFailure)
        {
            throw new InvalidOperationException($"Failed to initialize shards: {initializeResult.Error?.Message}");
        }
    }

    private PostgreSqlBuilder ConfigureContainerRuntime(PostgreSqlBuilder builder)
    {
        if (TryGetPodmanEndpoint(out var endpoint) && endpoint is not null)
        {
            // Disable Ryuk, as Podman does not support privileged helper containers by default.
            TestcontainersSettings.ResourceReaperEnabled = false;
            TestcontainersSettings.DockerSocketOverride = endpoint.OriginalString;

            var authConfig = new DockerEndpointAuthenticationConfiguration(
                endpoint,
                new AnonymousCredentials());

            return builder.WithDockerEndpoint(authConfig);
        }

        return builder;
    }

    private static bool TryGetPodmanEndpoint(out Uri? endpoint)
    {
        // Allow explicit override via environment variable.
        var podmanHost = Environment.GetEnvironmentVariable("PODMAN_HOST")
                         ?? Environment.GetEnvironmentVariable("DOCKER_HOST");

        if (!string.IsNullOrWhiteSpace(podmanHost) &&
            Uri.TryCreate(podmanHost, UriKind.Absolute, out endpoint))
        {
            return true;
        }

        if (TryGetKnownPodmanSocket(out var knownSocket) &&
            TryCreateUnixUri(knownSocket, out endpoint))
        {
            return true;
        }

        if (TryGetPodmanMachineSocket(out var machineSocket) &&
            TryCreateUnixUri(machineSocket, out endpoint))
        {
            return true;
        }

        try
        {
            if (TryRunCommand("podman", "info --format json", out var output) &&
                !string.IsNullOrWhiteSpace(output))
            {
                using var document = JsonDocument.Parse(output);
                if (document.RootElement.TryGetProperty("host", out var hostElement) &&
                    hostElement.TryGetProperty("remoteSocket", out var remoteSocket) &&
                    remoteSocket.TryGetProperty("path", out var pathElement))
                {
                    var socketPath = pathElement.GetString();
                    if (!string.IsNullOrWhiteSpace(socketPath) &&
                        TryCreateUnixUri(socketPath, out endpoint))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignored - fall back to default Docker configuration.
        }

        endpoint = null;
        return false;
    }

    private static bool IsDockerUnavailable(Exception ex)
    {
        if (ex is NullReferenceException)
        {
            return true;
        }

        if (ex is InvalidOperationException invalidOperation &&
            invalidOperation.Message.Contains("docker", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ex.InnerException is not null && IsDockerUnavailable(ex.InnerException);
    }

    private static bool TryGetPodmanMachineSocket(out string socketPath)
    {
        if (TryRunCommand("podman", "machine inspect --format '{{ .ConnectionInfo.PodmanSocket.Path }}'", out var output))
        {
            var candidate = output.Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                socketPath = candidate;
                return true;
            }
        }

        socketPath = string.Empty;
        return false;
    }

    private static bool TryGetKnownPodmanSocket(out string socketPath)
    {
        var candidates = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(home))
        {
            candidates.Add(Path.Combine(home, ".local", "share", "containers", "podman", "machine", "podman.sock"));
            candidates.Add(Path.Combine(home, ".local", "share", "containers", "podman", "machine", "podman-machine-default", "podman.sock"));
        }

        candidates.Add("/run/user/1000/podman/podman.sock");
        candidates.Add("/run/user/501/podman/podman.sock");
        candidates.Add("/run/podman/podman.sock");

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                socketPath = candidate;
                return true;
            }
        }

        socketPath = string.Empty;
        return false;
    }

    private static bool TryRunCommand(string fileName, string arguments, out string stdout)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                stdout = string.Empty;
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            var exited = process.WaitForExit(5000);

            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore cleanup failures.
                }

                stdout = string.Empty;
                return false;
            }

            if (process.ExitCode != 0)
            {
                stdout = string.Empty;
                return false;
            }

            stdout = output;
            return true;
        }
        catch
        {
            stdout = string.Empty;
            return false;
        }
    }

    private static bool TryCreateUnixUri(string socketPath, out Uri? uri)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            uri = null;
            return false;
        }

        var candidate = socketPath.StartsWith("unix://", StringComparison.OrdinalIgnoreCase)
            ? socketPath
            : $"unix://{socketPath}";

        candidate = candidate.Replace("\\", "/");

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }
}
