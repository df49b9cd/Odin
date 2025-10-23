using System.Data;
using Hugo;
using Microsoft.Extensions.Logging;
using Npgsql;

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
        {
            throw new ArgumentException("Connection string cannot be empty", nameof(connectionString));
        }

        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<Result<IDbConnection>> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await Result
            .TryAsync<IDbConnection>(async ct =>
            {
                var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);

                _logger.LogDebug("Opened PostgreSQL connection {ConnectionId}", connection.ProcessID);

                return connection;
            }, cancellationToken, ex =>
            {
                switch (ex)
                {
                    case NpgsqlException npgsqlEx:
                        _logger.LogError(npgsqlEx, "Failed to open PostgreSQL connection");
                        return Error.From($"Database connection failed: {npgsqlEx.Message}", "DB_CONNECTION_ERROR");
                    default:
                        _logger.LogError(ex, "Unexpected error opening PostgreSQL connection");
                        return Error.From($"Unexpected database error: {ex.Message}", "DB_UNEXPECTED_ERROR");
                }
            })
            .ConfigureAwait(false);
    }

    public async Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await (await CreateConnectionAsync(cancellationToken).ConfigureAwait(false))
            .ThenAsync(async (connection, ct) =>
            {
                using (connection)
                {
                    return await Result
                        .TryAsync(async innerCt =>
                        {
                            using var command = connection.CreateCommand();
                            command.CommandText = "SELECT 1";
                            var _ = await Task.Run(() => command.ExecuteScalar(), innerCt).ConfigureAwait(false);

                            _logger.LogInformation("PostgreSQL connection test succeeded");
                            return true;
                        }, ct, ex =>
                        {
                            _logger.LogError(ex, "PostgreSQL connection test failed");
                            return Error.From($"Connection test failed: {ex.Message}", "DB_TEST_FAILED");
                        })
                        .ConfigureAwait(false);
                }
            }, cancellationToken)
            .ConfigureAwait(false);
    }
}
