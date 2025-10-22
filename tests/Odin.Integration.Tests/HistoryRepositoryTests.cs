using System.Text.Json;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Repositories;
using Shouldly;
using Xunit;

namespace Odin.Integration.Tests;

[Collection("PostgresIntegration")]
public sealed class HistoryRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly PostgresFixture _fixture = fixture;
    private HistoryRepository? _historyRepository;
    private WorkflowExecutionRepository? _workflowRepository;
    private NamespaceRepository? _namespaceRepository;
    private Guid _namespaceId;
    private string _namespaceName = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _fixture.EnsureDockerIsRunning();
        _historyRepository ??= _fixture.CreateHistoryRepository();
        _workflowRepository ??= _fixture.CreateWorkflowExecutionRepository();
        _namespaceRepository ??= _fixture.CreateNamespaceRepository();

        await _fixture.ResetDatabaseAsync();

        _namespaceName = $"ns-{Guid.NewGuid():N}";
        var createdNamespace = await _namespaceRepository!.CreateAsync(
            new CreateNamespaceRequest { NamespaceName = _namespaceName },
            TestContext.Current.CancellationToken);

        createdNamespace.IsSuccess.ShouldBeTrue(createdNamespace.Error?.Message ?? "Failed to create namespace");
        _namespaceId = createdNamespace.Value.NamespaceId;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task AppendEventsAsync_AndRetrieveHistory()
    {
        var workflow = await CreateWorkflowAsync("wf-history-1");
        var events = new[]
        {
            CreateEvent(1, DateTimeOffset.UtcNow),
            CreateEvent(2, DateTimeOffset.UtcNow.AddSeconds(1))
        };

        var append = await Repository.AppendEventsAsync(
            _namespaceId.ToString(),
            workflow.WorkflowId,
            workflow.RunId.ToString(),
            events,
            TestContext.Current.CancellationToken);

        append.IsSuccess.ShouldBeTrue(append.Error?.Message ?? "AppendEventsAsync failed");

        var history = await Repository.GetHistoryAsync(
            _namespaceId.ToString(),
            workflow.WorkflowId,
            workflow.RunId.ToString(),
            cancellationToken: TestContext.Current.CancellationToken);

        history.IsSuccess.ShouldBeTrue(history.Error?.Message ?? "GetHistoryAsync failed");
        history.Value.Events.Count.ShouldBe(2);
        history.Value.LastEventId.ShouldBe(2);

        var count = await Repository.GetEventCountAsync(
            _namespaceId.ToString(),
            workflow.WorkflowId,
            workflow.RunId.ToString(),
            TestContext.Current.CancellationToken);
        count.Value.ShouldBe(2);
    }

    [Fact]
    public async Task AppendEventsAsync_WhenSequenceBreaks_ReturnsError()
    {
        var workflow = await CreateWorkflowAsync("wf-history-2");
        await Repository.AppendEventsAsync(
            _namespaceId.ToString(),
            workflow.WorkflowId,
            workflow.RunId.ToString(),
            new[] { CreateEvent(1, DateTimeOffset.UtcNow) },
            TestContext.Current.CancellationToken);

        var outOfSequence = await Repository.AppendEventsAsync(
            _namespaceId.ToString(),
            workflow.WorkflowId,
            workflow.RunId.ToString(),
            new[] { CreateEvent(3, DateTimeOffset.UtcNow) },
            TestContext.Current.CancellationToken);

        outOfSequence.IsFailure.ShouldBeTrue("Expected append with gap to fail");
    }

    private async Task<WorkflowExecution> CreateWorkflowAsync(string workflowId)
    {
        var execution = new WorkflowExecution
        {
            NamespaceId = _namespaceId,
            WorkflowId = workflowId,
            RunId = Guid.NewGuid(),
            WorkflowType = "HistoryWorkflow",
            TaskQueue = "history",
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ShardId = HashingUtilities.CalculateShardId(workflowId)
        };

        var created = await _workflowRepository!.CreateAsync(execution, TestContext.Current.CancellationToken);
        created.IsSuccess.ShouldBeTrue(created.Error?.Message ?? "Failed to create workflow execution");
        return created.Value;
    }

    private static HistoryEvent CreateEvent(long eventId, DateTimeOffset timestamp)
        => new()
        {
            EventId = eventId,
            EventType = WorkflowEventType.WorkflowExecutionStarted,
            EventTimestamp = timestamp,
            EventData = JsonDocument.Parse("""{"ok":true}""")
        };

    private HistoryRepository Repository
        => _historyRepository ?? throw new InvalidOperationException("History repository was not initialized.");
}
