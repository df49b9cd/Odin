using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Persistence.InMemory;
using Odin.Persistence.Interfaces;
using Odin.Persistence.Repositories;

namespace Odin.Persistence;

/// <summary>
/// Supported persistence providers.
/// </summary>
public enum PersistenceProvider
{
    /// <summary>
    /// Registers in-memory repository implementations.
    /// </summary>
    InMemory,

    /// <summary>
    /// Registers PostgreSQL-backed repository implementations.
    /// </summary>
    PostgreSql,

    /// <summary>
    /// Uses a custom registration delegate supplied by the caller.
    /// </summary>
    Custom
}

/// <summary>
/// Options used to configure persistence repository registrations.
/// </summary>
public sealed class PersistenceOptions
{
    /// <summary>
    /// Selected provider used for repository registration.
    /// </summary>
    public PersistenceProvider Provider { get; set; } = PersistenceProvider.InMemory;

    /// <summary>
    /// Connection string used by relational providers.
    /// </summary>
    public string? ConnectionString { get; set; }

    internal Action<IServiceCollection>? CustomRegistration { get; private set; }

    /// <summary>
    /// Configures the service collection using a custom provider.
    /// </summary>
    public void UseCustom(Action<IServiceCollection> configure)
    {
        Provider = PersistenceProvider.Custom;
        CustomRegistration = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    /// <summary>
    /// Configures the options for an in-memory provider.
    /// </summary>
    public void UseInMemory()
    {
        Provider = PersistenceProvider.InMemory;
    }

    /// <summary>
    /// Configures the options for the PostgreSQL provider.
    /// </summary>
    public void UsePostgreSql(string connectionString)
    {
        Provider = PersistenceProvider.PostgreSql;
        ConnectionString = connectionString;
    }
}

/// <summary>
/// Extension helpers for registering Odin persistence repositories.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers persistence services using configuration binding.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection("Persistence").Get<PersistenceOptions>()
            ?? new PersistenceOptions();

        return services.AddPersistence(options);
    }

    /// <summary>
    /// Registers persistence services using the provided options delegate.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        Action<PersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PersistenceOptions();
        configure(options);

        return services.AddPersistence(options);
    }

    /// <summary>
    /// Registers persistence services using the supplied options instance.
    /// </summary>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        switch (options.Provider)
        {
            case PersistenceProvider.InMemory:
                RegisterInMemory(services);
                break;
            case PersistenceProvider.PostgreSql:
                RegisterPostgreSql(services, options);
                break;
            case PersistenceProvider.Custom:
                if (options.CustomRegistration is null)
                {
                    throw new InvalidOperationException("Custom persistence provider requires a registration delegate.");
                }

                options.CustomRegistration(services);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Provider), options.Provider, "Unsupported persistence provider.");
        }

        return services;
    }

    private static void RegisterInMemory(IServiceCollection services)
    {
        services.AddSingleton<INamespaceRepository, InMemoryNamespaceRepository>();
        services.AddSingleton<IWorkflowExecutionRepository, InMemoryWorkflowExecutionRepository>();
        services.AddSingleton<IHistoryRepository, InMemoryHistoryRepository>();
        services.AddSingleton<IShardRepository, InMemoryShardRepository>();
        services.AddSingleton<ITaskQueueRepository, InMemoryTaskQueueRepository>();
        services.AddSingleton<IVisibilityRepository, InMemoryVisibilityRepository>();
    }

    private static void RegisterPostgreSql(IServiceCollection services, PersistenceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("A connection string must be provided for PostgreSQL persistence.");
        }

        services.AddSingleton<IDbConnectionFactory>(sp =>
            new PostgreSqlConnectionFactory(
                options.ConnectionString!,
                sp.GetRequiredService<ILogger<PostgreSqlConnectionFactory>>()));

        services.AddSingleton<INamespaceRepository, NamespaceRepository>();
        services.AddSingleton<IWorkflowExecutionRepository, WorkflowExecutionRepository>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();
        services.AddSingleton<IShardRepository, ShardRepository>();
        services.AddSingleton<ITaskQueueRepository, TaskQueueRepository>();
        services.AddSingleton<IVisibilityRepository, VisibilityRepository>();
    }
}
