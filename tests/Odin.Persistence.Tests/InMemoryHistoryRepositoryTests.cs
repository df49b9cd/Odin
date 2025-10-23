using System.Text.Json;
using Odin.Contracts;
using Odin.Persistence.InMemory;
using Shouldly;

namespace Odin.Persistence.Tests;

public class InMemoryHistoryRepositoryTests
{
    private static InMemoryHistoryRepository CreateRepository() => new();

    [Fact]
    public async Task AppendEventsAsync_WithSequentialIds_SucceedsAndAllowsRetrieval()
    {
        var repository = CreateRepository();
        var ct = TestContext.Current.CancellationToken;
        var events = new[]
        {
            CreateEvent(1, DateTimeOffset.UtcNow),
            CreateEvent(2, DateTimeOffset.UtcNow.AddSeconds(1))
        };

        var append = await repository.AppendEventsAsync("ns", "wf", "run", events, ct);
        append.IsSuccess.ShouldBeTrue(append.Error?.Message ?? "AppendEventsAsync failed");

        var history = await repository.GetHistoryAsync("ns", "wf", "run", fromEventId: 1, maxEvents: 10, cancellationToken: ct);
        history.IsSuccess.ShouldBeTrue(history.Error?.Message ?? "GetHistoryAsync failed");
        history.Value.Events.Count.ShouldBe(2);
        history.Value.FirstEventId.ShouldBe(1);

        var count = await repository.GetEventCountAsync("ns", "wf", "run", ct);
        count.IsSuccess.ShouldBeTrue(count.Error?.Message ?? "GetEventCountAsync failed");
        count.Value.ShouldBe(2);

        var valid = await repository.ValidateEventSequenceAsync("ns", "wf", "run", ct);
        valid.IsSuccess.ShouldBeTrue(valid.Error?.Message ?? "ValidateEventSequenceAsync failed");
        valid.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task AppendEventsAsync_WhenNonSequential_ReturnsError()
    {
        var repository = CreateRepository();
        var ct = TestContext.Current.CancellationToken;
        var firstBatch = await repository.AppendEventsAsync("ns", "wf", "run", new[] { CreateEvent(1, DateTimeOffset.UtcNow) }, ct);
        firstBatch.IsSuccess.ShouldBeTrue(firstBatch.Error?.Message ?? "Initial AppendEventsAsync failed");

        var outOfOrder = await repository.AppendEventsAsync("ns", "wf", "run", new[] { CreateEvent(3, DateTimeOffset.UtcNow) }, ct);

        outOfOrder.IsFailure.ShouldBeTrue("Expected failure for non-sequential event append");
    }

    [Fact]
    public async Task ArchiveOldEventsAsync_RemovesEventsOlderThanThreshold()
    {
        var repository = CreateRepository();
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        await repository.AppendEventsAsync("ns", "wf", "run", new[]
        {
            CreateEvent(1, now.AddDays(-10)),
            CreateEvent(2, now)
        }, ct);

        var archived = await repository.ArchiveOldEventsAsync("ns", olderThan: now.AddDays(-5), cancellationToken: ct);
        archived.IsSuccess.ShouldBeTrue(archived.Error?.Message ?? "ArchiveOldEventsAsync failed");
        archived.Value.ShouldBe(1);

        var count = await repository.GetEventCountAsync("ns", "wf", "run", ct);
        count.Value.ShouldBe(1);
    }

    private static HistoryEvent CreateEvent(long eventId, DateTimeOffset timestamp)
        => new()
        {
            EventId = eventId,
            EventType = WorkflowEventType.WorkflowExecutionStarted,
            EventTimestamp = timestamp,
            EventData = JsonDocument.Parse("""{"message":"ok"}""")
        };
}
