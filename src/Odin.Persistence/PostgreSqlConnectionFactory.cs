using System.Data;
using Hugo;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Hugo.Go;

namespace Odin.Persistence;

/// <summary>
/// PostgreSQL connection factory with connection pooling.
/// Uses Npgsql for high-performance PostgreSQL connectivity.
/// </summary>
public sealed class PostgreSqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSqlConnectionFactory> _logger;

    public DatabaseProvider Provider => DatabaseProvider.PostgreSQL;

    public PostgreSqlConnectionFactory(
        string connectionString,
        ILogger<PostgreSqlConnectionFactory> logger)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));

        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<Result<IDbConnection>> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            _logger.LogDebug("Opened PostgreSQL connection {ConnectionId}", connection.ProcessID);

            return Result.Ok<IDbConnection>(connection);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex, "Failed to open PostgreSQL connection");
            return Result.Fail<IDbConnection>(
                Error.From($"Database connection failed: {ex.Message}", "DB_CONNECTION_ERROR"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening PostgreSQL connection");
            return Result.Fail<IDbConnection>(
                Error.From($"Unexpected database error: {ex.Message}", "DB_UNEXPECTED_ERROR"));
        }
    }

    public async Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connectionResult = await CreateConnectionAsync(cancellationToken);
        if (connectionResult.IsFailure)
            return Result.Fail<bool>(connectionResult.Error!);

        using var connection = connectionResult.Value;
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            var _ = await Task.Run(() => command.ExecuteScalar(), cancellationToken);

            _logger.LogInformation("PostgreSQL connection test succeeded");
            return Result.Ok(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL connection test failed");
            return Result.Fail<bool>(
                Error.From($"Connection test failed: {ex.Message}", "DB_TEST_FAILED"));
        }
    }
}
