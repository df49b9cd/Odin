using Odin.Contracts;
using Odin.Persistence.InMemory;
using Shouldly;
using Xunit;

namespace Odin.Persistence.Tests;

public class InMemoryWorkflowExecutionRepositoryTests
{
    private static InMemoryWorkflowExecutionRepository CreateRepository()
        => new();

    [Fact]
    public async Task CreateAsync_AssignsDefaultsAndStoresExecution()
    {
        var repository = CreateRepository();
        var execution = CreateExecution();
        var ct = TestContext.Current.CancellationToken;

        var created = await repository.CreateAsync(execution, ct);

        created.IsSuccess.ShouldBeTrue(created.Error?.Message ?? "CreateAsync failed");
        created.Value.Version.ShouldBe(1);
        created.Value.ShardId.ShouldBeGreaterThanOrEqualTo(0);
        created.Value.ShardId.ShouldBeLessThan(512);

        var fetched = await repository.GetAsync(
            execution.NamespaceId.ToString(),
            execution.WorkflowId,
            execution.RunId.ToString(),
            ct);

        fetched.IsSuccess.ShouldBeTrue(fetched.Error?.Message ?? "GetAsync failed");
        fetched.Value.WorkflowId.ShouldBe(execution.WorkflowId);
    }

    [Fact]
    public async Task UpdateAsync_WithMatchingVersion_IncrementsVersion()
    {
        var repository = CreateRepository();
        var execution = CreateExecution();
        var ct = TestContext.Current.CancellationToken;

        var created = await repository.CreateAsync(execution, ct);
        created.IsSuccess.ShouldBeTrue(created.Error?.Message ?? "CreateAsync failed");

        var updated = await repository.UpdateAsync(
            created.Value with { WorkflowState = WorkflowState.Completed },
            expectedVersion: 1,
            ct);

        updated.IsSuccess.ShouldBeTrue(updated.Error?.Message ?? "UpdateAsync failed");
        updated.Value.Version.ShouldBe(2);
        updated.Value.WorkflowState.ShouldBe(WorkflowState.Completed);
    }

    [Fact]
    public async Task UpdateAsync_WithMismatchedVersion_ReturnsConcurrencyError()
    {
        var repository = CreateRepository();
        var execution = CreateExecution();
        var ct = TestContext.Current.CancellationToken;
        await repository.CreateAsync(execution, ct);

        var conflict = await repository.UpdateAsync(
            execution with { WorkflowState = WorkflowState.Failed },
            expectedVersion: 99,
            ct);

        conflict.IsFailure.ShouldBeTrue("Expected concurrency conflict to fail");
        conflict.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task TerminateAsync_SetsWorkflowStateAndCompletionMetadata()
    {
        var repository = CreateRepository();
        var execution = CreateExecution();
        var ct = TestContext.Current.CancellationToken;
        var created = await repository.CreateAsync(execution, ct);
        created.IsSuccess.ShouldBeTrue(created.Error?.Message ?? "CreateAsync failed");

        var terminate = await repository.TerminateAsync(
            execution.NamespaceId.ToString(),
            execution.WorkflowId,
            execution.RunId.ToString(),
            "test",
            ct);

        terminate.IsSuccess.ShouldBeTrue(terminate.Error?.Message ?? "TerminateAsync failed");

        var fetched = await repository.GetAsync(
            execution.NamespaceId.ToString(),
            execution.WorkflowId,
            execution.RunId.ToString(),
            ct);

        fetched.IsSuccess.ShouldBeTrue(fetched.Error?.Message ?? "GetAsync failed post terminate");
        fetched.Value.WorkflowState.ShouldBe(WorkflowState.Terminated);
        fetched.Value.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateWithEventIdAsync_AdvancesEventSequenceAndVersion()
    {
        var repository = CreateRepository();
        var execution = CreateExecution();
        var ct = TestContext.Current.CancellationToken;

        var created = await repository.CreateAsync(execution, ct);
        created.IsSuccess.ShouldBeTrue(created.Error?.Message ?? "CreateAsync failed");

        var toUpdate = created.Value with
        {
            LastProcessedEventId = 4
        };

        var updated = await repository.UpdateWithEventIdAsync(toUpdate, expectedVersion: 1, newEventId: 6, ct);

        updated.IsSuccess.ShouldBeTrue(updated.Error?.Message ?? "UpdateWithEventIdAsync failed");
        updated.Value.Version.ShouldBe(2);
        updated.Value.LastProcessedEventId.ShouldBe(4);
        updated.Value.NextEventId.ShouldBe(6);

        var fetched = await repository.GetAsync(
            execution.NamespaceId.ToString(),
            execution.WorkflowId,
            execution.RunId.ToString(),
            ct);

        fetched.IsSuccess.ShouldBeTrue(fetched.Error?.Message ?? "GetAsync failed");
        fetched.Value.NextEventId.ShouldBe(6);
    }

    private static WorkflowExecution CreateExecution()
    {
        var namespaceId = Guid.NewGuid();
        return new WorkflowExecution
        {
            NamespaceId = namespaceId,
            WorkflowId = $"workflow-{Guid.NewGuid():N}",
            RunId = Guid.NewGuid(),
            WorkflowType = "SampleWorkflow",
            TaskQueue = "default",
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
