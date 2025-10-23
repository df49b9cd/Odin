using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hugo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Sdk;
using Shouldly;
using static Hugo.Go;

namespace Odin.Sdk.Tests;

public class DeterministicReplayTests
{
    [Fact]
    public async Task WorkflowExecutor_ReplaysEffectsAndVersionDecisionsAcrossRuns()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowRuntime();
        services.AddWorkflow<ReplayWorkflow, ReplayInput, ReplayOutput>("deterministic-replay");
        services.AddTransient<ReplayActivity>();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<WorkflowExecutor>();
        var stateStore = new InMemoryDeterministicStateStore();

        ReplayActivity.InvocationCount = 0;

        var task = new WorkflowTask(
            Namespace: "default",
            WorkflowId: "deterministic-workflow",
            RunId: Guid.NewGuid().ToString("N"),
            TaskQueue: "determinism",
            WorkflowType: "deterministic-replay",
            Input: new ReplayInput("payload"),
            Metadata: new Dictionary<string, string> { ["source"] = "test" },
            InitialLogicalClock: 3,
            StateStore: stateStore);

        var first = await executor.ExecuteAsync(task, CancellationToken.None);
        first.IsSuccess.ShouldBeTrue(first.Error?.Message ?? "Initial execution failed.");
        var firstOutput = first.Value.ShouldBeOfType<ReplayOutput>();
        firstOutput.OperationId.ShouldNotBeNullOrEmpty();
        firstOutput.IsVersionNew.ShouldBeTrue();

        var replayTask = task with { ReplayCount = 1 };
        var replay = await executor.ExecuteAsync(replayTask, CancellationToken.None);
        replay.IsSuccess.ShouldBeTrue(replay.Error?.Message ?? "Replay execution failed.");
        var replayOutput = replay.Value.ShouldBeOfType<ReplayOutput>();
        replayOutput.OperationId.ShouldBe(firstOutput.OperationId);
        replayOutput.IsVersionNew.ShouldBeFalse();

        ReplayActivity.InvocationCount.ShouldBe(1);
    }

    private sealed record ReplayInput(string Payload);

    private sealed record ReplayOutput(string OperationId, bool IsVersionNew);

    private sealed class ReplayWorkflow(ReplayActivity activity) : WorkflowBase<ReplayInput, ReplayOutput>
    {
        protected override async Task<Result<ReplayOutput>> ExecuteAsync(
            WorkflowExecutionContext context,
            ReplayInput input,
            CancellationToken cancellationToken)
        {
            var version = RequireVersion("deterministic.feature", 1, 1);
            if (version.IsFailure)
            {
                return Result.Fail<ReplayOutput>(version.Error!);
            }

            var activityResult = await activity.ExecuteAsync(input, cancellationToken);
            if (activityResult.IsFailure)
            {
                return Result.Fail<ReplayOutput>(activityResult.Error!);
            }

            return Ok(new ReplayOutput(activityResult.Value.OperationId, version.Value.IsNew));
        }
    }

    private sealed record ReplayActivityResult(string OperationId);

    private sealed class ReplayActivity : ActivityBase<ReplayInput, ReplayActivityResult>
    {
        public static int InvocationCount;

        protected override Task<Result<ReplayActivityResult>> ExecuteAsync(
            WorkflowExecutionContext context,
            ReplayInput input,
            CancellationToken cancellationToken)
        {
            return WorkflowRuntime.CaptureAsync(
                $"deterministic-effect::{context.WorkflowId}",
                () =>
                {
                    var invocation = Interlocked.Increment(ref InvocationCount);
                    var operationId = $"op-{invocation}-{Guid.NewGuid():N}";
                    return Ok(new ReplayActivityResult(operationId));
                });
        }
    }
}
