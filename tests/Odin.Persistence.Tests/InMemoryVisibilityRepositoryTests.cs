using Odin.Contracts;
using Odin.Persistence.InMemory;
using Shouldly;

namespace Odin.Persistence.Tests;

public class InMemoryVisibilityRepositoryTests
{
    [Fact]
    public async Task UpsertListAndCount_WorkflowExecutions()
    {
        var repository = new InMemoryVisibilityRepository();
        var namespaceId = "ns-visibility";
        var ct = TestContext.Current.CancellationToken;

        var first = CreateExecutionInfo("workflow-1", DateTimeOffset.UtcNow.AddMinutes(-2), WorkflowStatus.Running);
        var second = CreateExecutionInfo("workflow-2", DateTimeOffset.UtcNow.AddMinutes(-1), WorkflowStatus.Completed);

        await repository.UpsertAsync(namespaceId, first.WorkflowId, first.RunId, first, ct);
        await repository.UpsertAsync(namespaceId, second.WorkflowId, second.RunId, second, ct);

        var list = await repository.ListAsync(new ListWorkflowExecutionsRequest
        {
            Namespace = namespaceId,
            PageSize = 10
        }, ct);

        list.IsSuccess.ShouldBeTrue(list.Error?.Message ?? "ListAsync failed");
        list.Value.Executions.Count.ShouldBe(2);
        list.Value.Executions.First().WorkflowId.ShouldBe("workflow-2"); // Sorted by StartTime desc

        var count = await repository.CountAsync(namespaceId, cancellationToken: ct);
        count.IsSuccess.ShouldBeTrue(count.Error?.Message ?? "CountAsync failed");
        count.Value.ShouldBe(2);
    }

    [Fact]
    public async Task SearchAsync_FiltersByQuery()
    {
        var repository = new InMemoryVisibilityRepository();
        var namespaceId = "ns-search";
        var ct = TestContext.Current.CancellationToken;

        var match = CreateExecutionInfo("order-123", DateTimeOffset.UtcNow, WorkflowStatus.Running);
        var other = CreateExecutionInfo("invoice-999", DateTimeOffset.UtcNow.AddMinutes(-5), WorkflowStatus.Running);

        await repository.UpsertAsync(namespaceId, match.WorkflowId, match.RunId, match, ct);
        await repository.UpsertAsync(namespaceId, other.WorkflowId, other.RunId, other, ct);

        var search = await repository.SearchAsync(namespaceId, "order", cancellationToken: ct);

        search.IsSuccess.ShouldBeTrue(search.Error?.Message ?? "SearchAsync failed");
        search.Value.Executions.Count.ShouldBe(1);
        search.Value.Executions.First().WorkflowId.ShouldBe("order-123");
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecordAndArchiveOldRemovesClosed()
    {
        var repository = new InMemoryVisibilityRepository();
        var namespaceId = "ns-cleanup";
        var ct = TestContext.Current.CancellationToken;
        var oldExecution = CreateExecutionInfo(
            "old-workflow",
            DateTimeOffset.UtcNow.AddDays(-10),
            WorkflowStatus.Completed,
            closeTime: DateTimeOffset.UtcNow.AddDays(-9));

        await repository.UpsertAsync(namespaceId, oldExecution.WorkflowId, oldExecution.RunId, oldExecution, ct);

        var archive = await repository.ArchiveOldRecordsAsync(namespaceId, DateTimeOffset.UtcNow.AddDays(-5), cancellationToken: ct);
        archive.IsSuccess.ShouldBeTrue(archive.Error?.Message ?? "ArchiveOldRecordsAsync failed");
        archive.Value.ShouldBe(1);

        var delete = await repository.DeleteAsync(namespaceId, oldExecution.WorkflowId, oldExecution.RunId, ct);
        delete.IsSuccess.ShouldBeTrue(delete.Error?.Message ?? "DeleteAsync failed");

        var count = await repository.CountAsync(namespaceId, cancellationToken: ct);
        count.Value.ShouldBe(0);
    }

    private static WorkflowExecutionInfo CreateExecutionInfo(
        string workflowId,
        DateTimeOffset startTime,
        WorkflowStatus status,
        DateTimeOffset? closeTime = null)
        => new()
        {
            WorkflowId = workflowId,
            RunId = Guid.NewGuid().ToString(),
            WorkflowType = "SampleWorkflow",
            TaskQueue = "default",
            Status = status,
            StartTime = startTime,
            CloseTime = closeTime,
            ExecutionDuration = closeTime.HasValue ? closeTime.Value - startTime : TimeSpan.FromMinutes(1),
            HistoryLength = 10,
            Memo = new Dictionary<string, object?> { ["key"] = "value" },
            SearchAttributes = new Dictionary<string, object?> { ["CustomKeyword"] = workflowId }
        };
}
