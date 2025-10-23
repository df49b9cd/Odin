using Odin.Contracts;
using Odin.Core;
using Odin.Persistence.Repositories;
using Shouldly;

namespace Odin.Integration.Tests;

[CollectionDefinition("PostgresIntegration")]
public sealed class PostgresIntegrationCollection : ICollectionFixture<PostgresFixture>;

[Collection("PostgresIntegration")]
public sealed class WorkflowExecutionRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly PostgresFixture _fixture = fixture;
    private WorkflowExecutionRepository? _repository;
    private Guid _namespaceId;

    public async ValueTask InitializeAsync()
    {
        _fixture.EnsureDockerIsRunning();

        _repository ??= _fixture.CreateWorkflowExecutionRepository();
        await _fixture.ResetDatabaseAsync();
        _namespaceId = await _fixture.CreateNamespaceAsync($"ns-{Guid.NewGuid():N}");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CreateAsync_PersistsWorkflowExecution()
    {
        var execution = CreateExecution("wf-create");

        var createResult = await Repository.CreateAsync(execution, TestContext.Current.CancellationToken);

        createResult.IsSuccess.ShouldBeTrue(createResult.Error?.Message ?? "CreateAsync failed");
        createResult.Value.NamespaceId.ShouldBe(_namespaceId);
        createResult.Value.RunId.ShouldNotBe(Guid.Empty);

        var getResult = await Repository.GetAsync(
            _namespaceId.ToString(),
            execution.WorkflowId,
            execution.RunId.ToString(),
            TestContext.Current.CancellationToken);

        getResult.IsSuccess.ShouldBeTrue(getResult.Error?.Message ?? "GetAsync failed");
        getResult.Value.NamespaceId.ShouldBe(_namespaceId);
        getResult.Value.Version.ShouldBe(1);
        getResult.Value.ShardId.ShouldBe(HashingUtilities.CalculateShardId(execution.WorkflowId));
    }

    [Fact]
    public async Task UpdateAsync_WithMatchingVersion_AdvancesVersionAndState()
    {
        var execution = CreateExecution("wf-update");
        var created = await Repository.CreateAsync(execution, TestContext.Current.CancellationToken);
        created.IsSuccess.ShouldBeTrue(created.Error?.Message ?? "CreateAsync failed");

        var updated = created.Value with
        {
            WorkflowState = WorkflowState.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            LastProcessedEventId = 10,
            NextEventId = created.Value.NextEventId + 1
        };

        var updateResult = await Repository.UpdateAsync(updated, (int)created.Value.Version, TestContext.Current.CancellationToken);

        updateResult.IsSuccess.ShouldBeTrue(updateResult.Error?.Message ?? "UpdateAsync failed");
        updateResult.Value.Version.ShouldBe(created.Value.Version + 1);
        updateResult.Value.WorkflowState.ShouldBe(WorkflowState.Completed);
        updateResult.Value.CompletedAt.ShouldNotBeNull();
        updateResult.Value.LastProcessedEventId.ShouldBe(10);
    }

    [Fact]
    public async Task UpdateAsync_WithMismatchedVersion_ReturnsConcurrencyError()
    {
        var execution = CreateExecution("wf-concurrency");
        await Repository.CreateAsync(execution, TestContext.Current.CancellationToken);

        var conflict = await Repository.UpdateAsync(
            execution with { TaskQueue = "conflict-queue" },
            expectedVersion: 99,
            TestContext.Current.CancellationToken);

        conflict.IsFailure.ShouldBeTrue("Concurrency conflict scenario did not fail as expected.");
        conflict.Error.ShouldNotBeNull();
        conflict.Error.Code.ShouldBe(OdinErrorCodes.ConcurrencyConflict, conflict.Error.Message);
    }

    [Fact]
    public async Task ListAsync_FiltersByWorkflowState()
    {
        var runningOne = await Repository.CreateAsync(CreateExecution("wf-running-1"), TestContext.Current.CancellationToken);
        runningOne.IsSuccess.ShouldBeTrue(runningOne.Error?.Message ?? "CreateAsync (runningOne) failed");

        var completed = await Repository.CreateAsync(CreateExecution("wf-completed"), TestContext.Current.CancellationToken);
        completed.IsSuccess.ShouldBeTrue(completed.Error?.Message ?? "CreateAsync (completed) failed");

        var runningTwo = await Repository.CreateAsync(CreateExecution("wf-running-2"), TestContext.Current.CancellationToken);
        runningTwo.IsSuccess.ShouldBeTrue(runningTwo.Error?.Message ?? "CreateAsync (runningTwo) failed");

        var completedUpdated = completed.Value with
        {
            WorkflowState = WorkflowState.Completed,
            CompletedAt = DateTimeOffset.UtcNow
        };
        var completeResult = await Repository.UpdateAsync(
            completedUpdated,
            (int)completed.Value.Version,
            TestContext.Current.CancellationToken);
        completeResult.IsSuccess.ShouldBeTrue(completeResult.Error?.Message ?? "UpdateAsync (completeResult) failed");

        var listResult = await Repository.ListAsync(
            _namespaceId.ToString(),
            WorkflowState.Running,
            pageSize: 10,
            cancellationToken: TestContext.Current.CancellationToken);

        listResult.IsSuccess.ShouldBeTrue(listResult.Error?.Message ?? "ListAsync failed");
        listResult.Value.Count.ShouldBe(2);
        listResult.Value.ShouldContain(x => x.WorkflowId == runningOne.Value.WorkflowId);
        listResult.Value.ShouldContain(x => x.WorkflowId == runningTwo.Value.WorkflowId);
        listResult.Value.All(x => x.Status == WorkflowStatus.Running).ShouldBeTrue();
    }

    [Fact]
    public async Task GetCurrentAsync_ReturnsMostRecentRun()
    {
        var baseExecution = CreateExecution("wf-current") with
        {
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastUpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var baseCreate = await Repository.CreateAsync(baseExecution, TestContext.Current.CancellationToken);
        baseCreate.IsSuccess.ShouldBeTrue(baseCreate.Error?.Message ?? "CreateAsync (baseExecution) failed");

        var newerExecution = baseExecution with
        {
            RunId = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };

        var newerCreate = await Repository.CreateAsync(newerExecution, TestContext.Current.CancellationToken);
        newerCreate.IsSuccess.ShouldBeTrue(newerCreate.Error?.Message ?? "CreateAsync (newerExecution) failed");

        var current = await Repository.GetCurrentAsync(
            _namespaceId.ToString(),
            baseExecution.WorkflowId,
            TestContext.Current.CancellationToken);

        current.IsSuccess.ShouldBeTrue(current.Error?.Message ?? "GetCurrentAsync failed");
        current.Value.RunId.ShouldBe(newerExecution.RunId);
    }

    [Fact]
    public async Task TerminateAsync_MarksWorkflowAsTerminated()
    {
        var execution = CreateExecution("wf-terminate") with { NextEventId = 7 };
        var createResult = await Repository.CreateAsync(execution, TestContext.Current.CancellationToken);
        createResult.IsSuccess.ShouldBeTrue(createResult.Error?.Message ?? "CreateAsync (terminate) failed");

        var terminate = await Repository.TerminateAsync(
            _namespaceId.ToString(),
            execution.WorkflowId,
            execution.RunId.ToString(),
            "test-termination",
            TestContext.Current.CancellationToken);

        terminate.IsSuccess.ShouldBeTrue(terminate.Error?.Message ?? "TerminateAsync failed");

        var refreshed = await Repository.GetAsync(
            _namespaceId.ToString(),
            execution.WorkflowId,
            execution.RunId.ToString(),
            TestContext.Current.CancellationToken);

        refreshed.IsSuccess.ShouldBeTrue(refreshed.Error?.Message ?? "GetAsync after termination failed");
        refreshed.Value.WorkflowState.ShouldBe(WorkflowState.Terminated);
        refreshed.Value.CompletionEventId.ShouldBe(execution.NextEventId);
        refreshed.Value.CompletedAt.ShouldNotBeNull();
    }

    private WorkflowExecution CreateExecution(string workflowId)
    {
        var now = DateTimeOffset.UtcNow;
        return new WorkflowExecution
        {
            NamespaceId = _namespaceId,
            WorkflowId = workflowId,
            RunId = Guid.NewGuid(),
            WorkflowType = "TestWorkflow",
            TaskQueue = "default",
            WorkflowState = WorkflowState.Running,
            ExecutionState = null,
            NextEventId = 1,
            LastProcessedEventId = 0,
            WorkflowTimeoutSeconds = null,
            RunTimeoutSeconds = null,
            TaskTimeoutSeconds = null,
            RetryPolicy = null,
            CronSchedule = null,
            ParentWorkflowId = null,
            ParentRunId = null,
            InitiatedId = null,
            CompletionEventId = null,
            Memo = null,
            SearchAttributes = null,
            AutoResetPoints = null,
            StartedAt = now,
            CompletedAt = null,
            LastUpdatedAt = now,
            ShardId = 0,
            Version = 0
        };
    }

    private WorkflowExecutionRepository Repository
        => _repository ?? throw new InvalidOperationException("Workflow execution repository has not been initialized.");
}
