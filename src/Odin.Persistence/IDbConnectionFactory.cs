using System.Data;
using Hugo;

namespace Odin.Persistence;

/// <summary>
/// Factory for creating database connections with proper lifecycle management.
/// Supports PostgreSQL and MySQL providers.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates and opens a new database connection.
    /// </summary>
    Task<Result<IDbConnection>> CreateConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the database provider type (PostgreSQL or MySQL).
    /// </summary>
    DatabaseProvider Provider { get; }

    /// <summary>
    /// Tests connectivity to the database.
    /// </summary>
    Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Supported database providers.
/// </summary>
public enum DatabaseProvider
{
    PostgreSQL,
    MySQL
}
