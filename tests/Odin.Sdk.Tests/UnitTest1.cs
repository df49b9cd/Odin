using Hugo;
using Shouldly;
using static Hugo.Go;

namespace Odin.Sdk.Tests;

public class WorkflowRuntimeTests
{
    [Fact]
    public void WorkflowRuntimeScope_ProvidesContextAndCleansUp()
    {
        var options = CreateOptions();

        WorkflowRuntime.HasCurrent.ShouldBeFalse();

        using (WorkflowRuntime.Initialize(options))
        {
            WorkflowRuntime.HasCurrent.ShouldBeTrue();

            var context = WorkflowRuntime.Context;
            context.Namespace.ShouldBe(options.Namespace);
            context.WorkflowId.ShouldBe(options.WorkflowId);
            context.RunId.ShouldBe(options.RunId);
            context.TaskQueue.ShouldBe(options.TaskQueue);

            WorkflowRuntime.TryGetMetadata("customer", out var customer).ShouldBeTrue();
            customer.ShouldBe("odin");
        }

        WorkflowRuntime.HasCurrent.ShouldBeFalse();
    }

    [Fact]
    public async Task WorkflowRuntime_CaptureAsync_ReplaysStoredResult()
    {
        using var scope = WorkflowRuntime.Initialize(CreateOptions());

        var invocations = 0;
        var first = await WorkflowRuntime.CaptureAsync(
            "side-effect",
            async ct =>
            {
                invocations++;
                await Task.Delay(10, ct);
                return Ok(42);
            },
            CancellationToken.None);

        first.IsSuccess.ShouldBeTrue(first.Error?.Message ?? "Initial capture failed.");
        first.Value.ShouldBe(42);
        invocations.ShouldBe(1);

        var second = await WorkflowRuntime.CaptureAsync<int>(
            "side-effect",
            _ => throw new InvalidOperationException("Effect should not be re-executed."),
            CancellationToken.None);

        second.IsSuccess.ShouldBeTrue(second.Error?.Message ?? "Replay capture failed.");
        second.Value.ShouldBe(42);
        invocations.ShouldBe(1);
    }

    [Fact]
    public void WorkflowRuntime_RequireVersion_PersistsDecision()
    {
        using var scope = WorkflowRuntime.Initialize(CreateOptions());

        var first = WorkflowRuntime.RequireVersion("change-1", 2, 5, _ => 3);
        first.IsSuccess.ShouldBeTrue(first.Error?.Message ?? "Initial version gate failed.");
        first.Value.Version.ShouldBe(3);
        first.Value.IsNew.ShouldBeTrue();

        var second = WorkflowRuntime.RequireVersion("change-1", 1, 5);
        second.IsSuccess.ShouldBeTrue(second.Error?.Message ?? "Subsequent version gate failed.");
        second.Value.Version.ShouldBe(first.Value.Version);
        second.Value.IsNew.ShouldBeFalse();
    }

    [Fact]
    public async Task WorkflowBase_ExecuteAsync_UsesContext()
    {
        using var scope = WorkflowRuntime.Initialize(CreateOptions());
        var workflow = new IncrementWorkflow();

        var result = await workflow.ExecuteAsync(41, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message ?? "Workflow execution failed.");
        result.Value.ShouldBe(42);
        workflow.LastNamespace.ShouldBe("odin");
    }

    [Fact]
    public async Task ActivityBase_ExecuteAsync_UsesContext()
    {
        using var scope = WorkflowRuntime.Initialize(CreateOptions());
        var activity = new FormatActivity();

        var result = await activity.ExecuteAsync(123, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message ?? "Activity execution failed.");
        result.Value.ShouldBe("123@odin");
    }

    private static WorkflowRuntimeOptions CreateOptions()
        => new()
        {
            Namespace = "odin",
            WorkflowId = "wf-123",
            RunId = Guid.NewGuid().ToString("N"),
            TaskQueue = "primary",
            Metadata = new Dictionary<string, string> { ["customer"] = "odin" },
            StartedAt = DateTimeOffset.UtcNow,
            InitialLogicalClock = 5,
            ReplayCount = 1
        };

    private sealed class IncrementWorkflow : WorkflowBase<int, int>
    {
        public string? LastNamespace { get; private set; }

        protected override Task<Result<int>> ExecuteAsync(
            WorkflowExecutionContext context,
            int input,
            CancellationToken cancellationToken)
        {
            LastNamespace = context.Namespace;
            return Task.FromResult(Ok(input + 1));
        }
    }

    private sealed class FormatActivity : ActivityBase<int, string>
    {
        protected override Task<Result<string>> ExecuteAsync(
            WorkflowExecutionContext context,
            int input,
            CancellationToken cancellationToken)
        {
            var formatted = $"{input}@{context.Namespace}";
            return Task.FromResult(Ok(formatted));
        }
    }
}
