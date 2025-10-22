using System.Collections.Generic;
using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Repositories;
using Shouldly;
using Xunit;

namespace Odin.Integration.Tests;

[Collection("PostgresIntegration")]
public sealed class VisibilityRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly PostgresFixture _fixture = fixture;
    private VisibilityRepository? _visibilityRepository;
    private NamespaceRepository? _namespaceRepository;
    private WorkflowExecutionRepository? _workflowRepository;
    private Guid _namespaceId;
    private string _namespaceName = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _fixture.EnsureDockerIsRunning();
        _visibilityRepository ??= _fixture.CreateVisibilityRepository();
        _namespaceRepository ??= _fixture.CreateNamespaceRepository();
        _workflowRepository ??= _fixture.CreateWorkflowExecutionRepository();

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
    public async Task UpsertListAndCount_VisibilityRecords()
    {
        var workflow = await CreateWorkflowAsync("vis-wf-1");
        var info = CreateVisibilityInfo(workflow.WorkflowId, workflow.RunId.ToString(), WorkflowStatus.Running, DateTimeOffset.UtcNow);

        var upsert = await Repository.UpsertAsync(
            _namespaceId.ToString(),
            workflow.WorkflowId,
            workflow.RunId.ToString(),
            info,
            TestContext.Current.CancellationToken);
        upsert.IsSuccess.ShouldBeTrue(upsert.Error?.Message ?? "UpsertAsync failed");

        var list = await Repository.ListAsync(new ListWorkflowExecutionsRequest
        {
            Namespace = _namespaceId.ToString(),
            PageSize = 10
        }, TestContext.Current.CancellationToken);
        list.IsSuccess.ShouldBeTrue(list.Error?.Message ?? "ListAsync failed");
        list.Value.Executions.Count.ShouldBe(1);
        list.Value.Executions[0].WorkflowId.ShouldBe(workflow.WorkflowId);

        var count = await Repository.CountAsync(_namespaceId.ToString(), cancellationToken: TestContext.Current.CancellationToken);
        count.IsSuccess.ShouldBeTrue(count.Error?.Message ?? "CountAsync failed");
        count.Value.ShouldBe(1);
    }

    [Fact]
    public async Task SearchAndDelete_WorkflowVisibility()
    {
        var workflow = await CreateWorkflowAsync("vis-wf-2");
        var info = CreateVisibilityInfo(workflow.WorkflowId, workflow.RunId.ToString(), WorkflowStatus.Completed, DateTimeOffset.UtcNow, closeTime: DateTimeOffset.UtcNow);
        await Repository.UpsertAsync(
            _namespaceId.ToString(),
            workflow.WorkflowId,
            workflow.RunId.ToString(),
            info,
            TestContext.Current.CancellationToken);

        var search = await Repository.SearchAsync(
            _namespaceId.ToString(),
            query: "vis-wf-2",
            pageSize: 10,
            cancellationToken: TestContext.Current.CancellationToken);
        search.IsSuccess.ShouldBeTrue(search.Error?.Message ?? "SearchAsync failed");
        search.Value.Executions.Count.ShouldBe(1);

        var delete = await Repository.DeleteAsync(
            _namespaceId.ToString(),
            workflow.WorkflowId,
            workflow.RunId.ToString(),
            TestContext.Current.CancellationToken);
        delete.IsSuccess.ShouldBeTrue(delete.Error?.Message ?? "DeleteAsync failed");

        var count = await Repository.CountAsync(_namespaceId.ToString(), cancellationToken: TestContext.Current.CancellationToken);
        count.Value.ShouldBe(0);
    }

    private async Task<WorkflowExecution> CreateWorkflowAsync(string workflowId)
    {
        var execution = new WorkflowExecution
        {
            NamespaceId = _namespaceId,
            WorkflowId = workflowId,
            RunId = Guid.NewGuid(),
            WorkflowType = "VisibilityWorkflow",
            TaskQueue = "visibility",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            LastUpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ShardId = HashingUtilities.CalculateShardId(workflowId)
        };

        var created = await _workflowRepository!.CreateAsync(execution, TestContext.Current.CancellationToken);
        created.IsSuccess.ShouldBeTrue(created.Error?.Message ?? "Failed to create workflow execution");
        return created.Value;
    }

    private static WorkflowExecutionInfo CreateVisibilityInfo(
        string workflowId,
        string runId,
        WorkflowStatus status,
        DateTimeOffset startTime,
        DateTimeOffset? closeTime = null)
        => new()
        {
            WorkflowId = workflowId,
            RunId = runId,
            WorkflowType = "VisibilityWorkflow",
            TaskQueue = "visibility",
            Status = status,
            StartTime = startTime,
            CloseTime = closeTime,
            ExecutionDuration = closeTime.HasValue ? closeTime.Value - startTime : TimeSpan.FromMinutes(1),
            HistoryLength = 5,
            Memo = new Dictionary<string, object?> { ["memo"] = "value" },
            SearchAttributes = new Dictionary<string, object?> { ["Query"] = workflowId }
        };

    private VisibilityRepository Repository
        => _visibilityRepository ?? throw new InvalidOperationException("Visibility repository was not initialized.");
}
