using System.Collections.Concurrent;
using System.Text.Json;
using Hugo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Odin.Core;

namespace Odin.Sdk;

public interface IWorkflowRegistry
{
    bool TryGetRegistration(string workflowType, out WorkflowRegistration? registration);
}

public sealed class WorkflowRegistry : IWorkflowRegistry
{
    private readonly ConcurrentDictionary<string, WorkflowRegistration> _registrations;

    public WorkflowRegistry(IEnumerable<WorkflowRegistrationDescriptor> descriptors)
    {
        _registrations = new ConcurrentDictionary<string, WorkflowRegistration>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            var registration = descriptor.CreateRegistration() ?? throw new InvalidOperationException($"Workflow registration for '{descriptor.WorkflowType}' could not be created.");
            var workflowType = descriptor.WorkflowType ?? throw new InvalidOperationException("Workflow type name cannot be null.");
            if (!_registrations.TryAdd(workflowType, registration!))
            {
                throw new InvalidOperationException($"Workflow type '{workflowType}' is already registered.");
            }
        }
    }

    public bool TryGetRegistration(string workflowType, out WorkflowRegistration? registration)
        => _registrations.TryGetValue(workflowType, out registration);
}

public sealed record WorkflowRegistration(
    Type InputType,
    Type OutputType,
    Func<IServiceProvider, WorkflowRuntimeOptions, object?, CancellationToken, Task<Result<object?>>> Executor);

public sealed record WorkflowRegistrationDescriptor(
    string WorkflowType,
    Type WorkflowTypeDefinition,
    Type InputType,
    Type OutputType,
    Func<IServiceProvider, WorkflowRuntimeOptions, object?, CancellationToken, Task<Result<object?>>> Executor)
{
    internal WorkflowRegistration CreateRegistration()
        => new(InputType, OutputType, Executor);
}

public static class WorkflowRegistrationExtensions
{
    public static IServiceCollection AddWorkflowRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<WorkflowRegistry>();
        services.TryAddSingleton<IWorkflowRegistry>(sp => sp.GetRequiredService<WorkflowRegistry>());
        services.TryAddSingleton<WorkflowExecutor>();
        return services;
    }

    public static IServiceCollection AddWorkflow<TWorkflow, TInput, TOutput>(
        this IServiceCollection services,
        string workflowType)
        where TWorkflow : class, IWorkflow<TInput, TOutput>
    {
        if (string.IsNullOrWhiteSpace(workflowType))
        {
            throw new ArgumentException("Workflow type name must be provided.", nameof(workflowType));
        }

        services.AddTransient<TWorkflow>();
        services.AddWorkflowRuntime();
        services.AddSingleton(new WorkflowRegistrationDescriptor(
            workflowType,
            typeof(TWorkflow),
            typeof(TInput),
            typeof(TOutput),
            static async (provider, options, input, cancellationToken) =>
            {
                var workflow = provider.GetRequiredService<TWorkflow>();
                var serializer = options.SerializerOptions ?? JsonOptions.Default;
                var typedInput = ConvertInput<TInput>(input, serializer);

                using var scope = WorkflowRuntime.Initialize(options);
                var result = await workflow.ExecuteAsync(typedInput, cancellationToken).ConfigureAwait(false);

                if (result.IsFailure)
                {
                    return Result.Fail<object?>(result.Error!);
                }

                return Result.Ok<object?>(result.Value);
            }));

        return services;
    }

    private static TInput ConvertInput<TInput>(object? value, JsonSerializerOptions serializerOptions)
    {
        if (value is null)
        {
            if (default(TInput) is null)
            {
                return default!;
            }

            throw new InvalidOperationException($"Workflow input of type {typeof(TInput).Name} cannot be null.");
        }

        if (value is TInput typed)
        {
            return typed;
        }

        if (value is JsonElement element)
        {
            return element.Deserialize<TInput>(serializerOptions)
                   ?? throw new InvalidOperationException($"Unable to deserialize workflow input to {typeof(TInput).Name}.");
        }

        if (value is string json)
        {
            return JsonSerializer.Deserialize<TInput>(json, serializerOptions)
                   ?? throw new InvalidOperationException($"Unable to deserialize workflow input to {typeof(TInput).Name}.");
        }

        throw new InvalidOperationException($"Workflow input must be assignable to {typeof(TInput).Name}, but was {value.GetType().Name}.");
    }
}
