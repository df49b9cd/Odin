using Hugo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using static Hugo.Go;

namespace Odin.Sdk.Tests;

public class WorkflowExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RunsRegisteredWorkflow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflow<TestWorkflow, TestInput, TestOutput>("test-workflow");
        services.AddTransient<TestActivity>();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<WorkflowExecutor>();

        var task = new WorkflowTask(
            Namespace: "default",
            WorkflowId: "wf-123",
            RunId: Guid.NewGuid().ToString("N"),
            TaskQueue: "test",
            WorkflowType: "test-workflow",
            Input: new TestInput(InitialValue: 10),
            Metadata: new Dictionary<string, string> { ["tenant"] = "odin" });

        var result = await executor.ExecuteAsync(task, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message ?? "Workflow execution failed.");
        var typed = result.Value.ShouldBeOfType<TestOutput>();
        typed.Result.ShouldBe(42);
    }

    [Fact]
    public async Task ExecuteAsync_FailsWhenWorkflowNotRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowRuntime();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<WorkflowExecutor>();

        var task = new WorkflowTask(
            Namespace: "default",
            WorkflowId: "wf-missing",
            RunId: Guid.NewGuid().ToString("N"),
            TaskQueue: "test",
            WorkflowType: "missing",
            Input: null);

        var result = await executor.ExecuteAsync(task, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldNotBeNull();
    }

    private sealed record TestInput(int InitialValue);

    private sealed record TestOutput(int Result);

    private sealed class TestWorkflow(TestActivity activity, ILogger<TestWorkflow> logger)
        : WorkflowBase<TestInput, TestOutput>
    {
        protected override async Task<Result<TestOutput>> ExecuteAsync(
            WorkflowExecutionContext context,
            TestInput input,
            CancellationToken cancellationToken)
        {
            var versionResult = RequireVersion("test-workflow.version", 1, 2);
            versionResult.IsSuccess.ShouldBeTrue(versionResult.Error?.Message ?? "Version gate failed");
            versionResult.Value.IsNew.ShouldBeTrue();

            var activityResult = await activity.ExecuteAsync(input, cancellationToken);
            if (activityResult.IsFailure)
            {
                return Result.Fail<TestOutput>(activityResult.Error!);
            }

            context.Tick();
            logger.LogInformation("Workflow {WorkflowId} completed.", context.WorkflowId);

            return Ok(new TestOutput(activityResult.Value.Total));
        }
    }

    private sealed class TestActivity : ActivityBase<TestInput, TestActivityOutput>
    {
        protected override Task<Result<TestActivityOutput>> ExecuteAsync(
            WorkflowExecutionContext context,
            TestInput input,
            CancellationToken cancellationToken)
        {
            return CaptureAsync(
                $"test-activity::{context.WorkflowId}",
                _ => Task.FromResult(Ok(new TestActivityOutput(input.InitialValue * 4 + 2))),
                cancellationToken);
        }
    }

    private sealed record TestActivityOutput(int Total);
}
