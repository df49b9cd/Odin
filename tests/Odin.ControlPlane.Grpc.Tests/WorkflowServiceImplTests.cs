using Grpc.Core;
using Hugo;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Odin.ControlPlane.Grpc.Services;
using Odin.Core;
using Odin.Persistence.Interfaces;
using Shouldly;
using static Hugo.Go;
using DomainWorkflowExecution = Odin.Contracts.WorkflowExecution;
using DomainWorkflowState = Odin.Contracts.WorkflowState;
using GrpcContracts = Odin.ControlPlane.Grpc;

namespace Odin.ControlPlane.Grpc.Tests;

public sealed class WorkflowServiceImplTests
{
    private static WorkflowServiceImpl CreateService(
        IWorkflowExecutionRepository? repository = null,
        ILogger<WorkflowServiceImpl>? logger = null)
    {
        repository ??= Substitute.For<IWorkflowExecutionRepository>();
        logger ??= Substitute.For<ILogger<WorkflowServiceImpl>>();
        return new WorkflowServiceImpl(repository, logger);
    }

    private static ServerCallContext CreateContext(CancellationToken cancellationToken = default)
    {
        var token = cancellationToken == default
            ? (TestContext.Current?.CancellationToken ?? CancellationToken.None)
            : cancellationToken;

        return new FakeServerCallContext(token);
    }

    #region StartWorkflow

    [Fact]
    public async Task StartWorkflow_WhenWorkflowTypeMissing_ThrowsInvalidArgument()
    {
        var service = CreateService();
        var request = new GrpcContracts.StartWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowType = "",
            TaskQueue = "queue"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.StartWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task StartWorkflow_WhenTaskQueueMissing_ThrowsInvalidArgument()
    {
        var service = CreateService();
        var request = new GrpcContracts.StartWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowType = "workflow-type",
            TaskQueue = ""
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.StartWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task StartWorkflow_WhenNamespaceInvalid_ThrowsInvalidArgument()
    {
        var service = CreateService();
        var request = new GrpcContracts.StartWorkflowRequest
        {
            NamespaceId = "not-a-guid",
            WorkflowType = "workflow-type",
            TaskQueue = "queue"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.StartWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task StartWorkflow_WhenRepositoryFails_ThrowsMappedRpcException()
    {
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.CalculateShardId(Arg.Any<string>()).Returns(1);
        repository.CreateAsync(Arg.Any<DomainWorkflowExecution>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<DomainWorkflowExecution>(
                Error.From("duplicate", OdinErrorCodes.WorkflowAlreadyExists)));

        var service = CreateService(repository);
        var request = new GrpcContracts.StartWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowType = "workflow-type",
            TaskQueue = "queue"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.StartWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task StartWorkflow_Success_ReturnsIdentifiers()
    {
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.CalculateShardId(Arg.Any<string>()).Returns(17);
        DomainWorkflowExecution? persisted = null;
        repository.CreateAsync(Arg.Do<DomainWorkflowExecution>(exec => persisted = exec), Arg.Any<CancellationToken>())
            .Returns(call => Result.Ok(call.Arg<DomainWorkflowExecution>()));

        var service = CreateService(repository);
        var request = new GrpcContracts.StartWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowType = "workflow-type",
            TaskQueue = "queue"
        };

        var response = await service.StartWorkflow(request, CreateContext(TestContext.Current.CancellationToken));

        response.WorkflowId.ShouldNotBeNullOrWhiteSpace();
        response.RunId.ShouldNotBeNullOrWhiteSpace();
        persisted.ShouldNotBeNull();
        persisted!.WorkflowId.ShouldBe(response.WorkflowId);
        persisted.RunId.ShouldBe(Guid.Parse(response.RunId));
        persisted.ShardId.ShouldBe(17);
        await repository.Received(1)
            .CreateAsync(Arg.Any<DomainWorkflowExecution>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetWorkflow

    [Fact]
    public async Task GetWorkflow_WhenWorkflowIdMissing_ThrowsInvalidArgument()
    {
        var service = CreateService();
        var request = new GrpcContracts.GetWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = ""
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.GetWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task GetWorkflow_WhenNamespaceInvalid_ThrowsInvalidArgument()
    {
        var service = CreateService();
        var request = new GrpcContracts.GetWorkflowRequest
        {
            NamespaceId = "invalid",
            WorkflowId = "wf"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.GetWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task GetWorkflow_WhenRepositoryReturnsFailure_ThrowsNotFound()
    {
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetCurrentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<DomainWorkflowExecution>(OdinErrors.WorkflowNotFound("wf")));

        var service = CreateService(repository);
        var request = new GrpcContracts.GetWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = "wf"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.GetWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.NotFound);
        await repository.Received(1)
            .GetCurrentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWorkflow_WithRunId_Success()
    {
        var namespaceId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var execution = new DomainWorkflowExecution
        {
            NamespaceId = namespaceId,
            WorkflowId = "wf",
            RunId = runId,
            WorkflowType = "type",
            TaskQueue = "queue",
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ShardId = 12
        };

        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetAsync(namespaceId.ToString(), execution.WorkflowId, runId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(execution));

        var service = CreateService(repository);
        var request = new GrpcContracts.GetWorkflowRequest
        {
            NamespaceId = namespaceId.ToString(),
            WorkflowId = "wf",
            RunId = runId.ToString()
        };

        var response = await service.GetWorkflow(request, CreateContext(TestContext.Current.CancellationToken));

        response.Execution.ShouldNotBeNull();
        response.Execution.WorkflowId.ShouldBe("wf");
        response.Execution.RunId.ShouldBe(runId.ToString());
        await repository.Received(1)
            .GetAsync(namespaceId.ToString(), "wf", runId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWorkflow_WithoutRunId_Success()
    {
        var namespaceId = Guid.NewGuid();
        var execution = new DomainWorkflowExecution
        {
            NamespaceId = namespaceId,
            WorkflowId = "wf",
            RunId = Guid.NewGuid(),
            WorkflowType = "type",
            TaskQueue = "queue",
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ShardId = 12
        };

        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetCurrentAsync(namespaceId.ToString(), execution.WorkflowId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(execution));

        var service = CreateService(repository);
        var request = new GrpcContracts.GetWorkflowRequest
        {
            NamespaceId = namespaceId.ToString(),
            WorkflowId = "wf",
            RunId = ""
        };

        var response = await service.GetWorkflow(request, CreateContext(TestContext.Current.CancellationToken));

        response.Execution.ShouldNotBeNull();
        response.Execution.WorkflowId.ShouldBe("wf");
        await repository.Received(1)
            .GetCurrentAsync(namespaceId.ToString(), "wf", Arg.Any<CancellationToken>());
    }

    #endregion

    #region SignalWorkflow

    [Fact]
    public async Task SignalWorkflow_WhenSignalNameMissing_ThrowsInvalidArgument()
    {
        var service = CreateService();
        var request = new GrpcContracts.SignalWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = "wf",
            SignalName = ""
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.SignalWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task SignalWorkflow_WhenWorkflowNotFound_ThrowsNotFound()
    {
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<DomainWorkflowExecution>(OdinErrors.WorkflowNotFound("wf")));

        var service = CreateService(repository);
        var request = new GrpcContracts.SignalWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = "wf",
            RunId = Guid.NewGuid().ToString(),
            SignalName = "signal"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.SignalWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Fact]
    public async Task SignalWorkflow_WhenWorkflowNotRunning_ThrowsFailedPrecondition()
    {
        var namespaceId = Guid.NewGuid().ToString();
        var runId = Guid.NewGuid();
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetAsync(namespaceId, "wf", runId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DomainWorkflowExecution
            {
                NamespaceId = Guid.Parse(namespaceId),
                WorkflowId = "wf",
                RunId = runId,
                WorkflowType = "type",
                TaskQueue = "queue",
                WorkflowState = DomainWorkflowState.Terminated,
                StartedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                ShardId = 4
            }));

        var service = CreateService(repository);
        var request = new GrpcContracts.SignalWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowId = "wf",
            RunId = runId.ToString(),
            SignalName = "signal"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.SignalWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task SignalWorkflow_Success()
    {
        var namespaceId = Guid.NewGuid().ToString();
        var runId = Guid.NewGuid();
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetAsync(namespaceId, "wf", runId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DomainWorkflowExecution
            {
                NamespaceId = Guid.Parse(namespaceId),
                WorkflowId = "wf",
                RunId = runId,
                WorkflowType = "type",
                TaskQueue = "queue",
                WorkflowState = DomainWorkflowState.Running,
                StartedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                ShardId = 4
            }));

        var service = CreateService(repository);
        var request = new GrpcContracts.SignalWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowId = "wf",
            RunId = runId.ToString(),
            SignalName = "signal"
        };

        var response = await service.SignalWorkflow(request, CreateContext(TestContext.Current.CancellationToken));
        response.ShouldNotBeNull();
    }

    #endregion

    #region TerminateWorkflow

    [Fact]
    public async Task TerminateWorkflow_WhenWorkflowIdMissing_ThrowsInvalidArgument()
    {
        var service = CreateService();
        var request = new GrpcContracts.TerminateWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = ""
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.TerminateWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task TerminateWorkflow_WhenWorkflowNotFound_ThrowsNotFound()
    {
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<DomainWorkflowExecution>(OdinErrors.WorkflowNotFound("wf")));

        var service = CreateService(repository);
        var request = new GrpcContracts.TerminateWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = "wf",
            RunId = Guid.NewGuid().ToString()
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.TerminateWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Fact]
    public async Task TerminateWorkflow_WhenNotRunning_ThrowsFailedPrecondition()
    {
        var namespaceId = Guid.NewGuid().ToString();
        var runId = Guid.NewGuid().ToString();
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetAsync(namespaceId, "wf", runId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DomainWorkflowExecution
            {
                NamespaceId = Guid.Parse(namespaceId),
                WorkflowId = "wf",
                RunId = Guid.Parse(runId),
                WorkflowType = "type",
                TaskQueue = "queue",
                WorkflowState = DomainWorkflowState.Terminated,
                StartedAt = DateTimeOffset.UtcNow,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                ShardId = 4
            }));

        var service = CreateService(repository);
        var request = new GrpcContracts.TerminateWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowId = "wf",
            RunId = runId,
            Reason = "test"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.TerminateWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task TerminateWorkflow_WhenTerminateFails_ThrowsInternal()
    {
        var namespaceId = Guid.NewGuid().ToString();
        var runId = Guid.NewGuid().ToString();
        var execution = new DomainWorkflowExecution
        {
            NamespaceId = Guid.Parse(namespaceId),
            WorkflowId = "wf",
            RunId = Guid.Parse(runId),
            WorkflowType = "type",
            TaskQueue = "queue",
            WorkflowState = DomainWorkflowState.Running,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ShardId = 4
        };

        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetAsync(namespaceId, "wf", runId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(execution));
        repository.TerminateAsync(namespaceId, "wf", runId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<Unit>(Error.From("db error", OdinErrorCodes.PersistenceError)));

        var service = CreateService(repository);
        var request = new GrpcContracts.TerminateWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowId = "wf",
            RunId = runId,
            Reason = "test"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.TerminateWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.Internal);
    }

    [Fact]
    public async Task TerminateWorkflow_Success()
    {
        var namespaceId = Guid.NewGuid().ToString();
        var runId = Guid.NewGuid().ToString();
        var execution = new DomainWorkflowExecution
        {
            NamespaceId = Guid.Parse(namespaceId),
            WorkflowId = "wf",
            RunId = Guid.Parse(runId),
            WorkflowType = "type",
            TaskQueue = "queue",
            WorkflowState = DomainWorkflowState.Running,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ShardId = 4
        };

        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetAsync(namespaceId, "wf", runId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(execution));
        repository.TerminateAsync(namespaceId, "wf", runId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(Unit.Value));

        var service = CreateService(repository);
        var request = new GrpcContracts.TerminateWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowId = "wf",
            RunId = runId,
            Reason = "test"
        };

        var response = await service.TerminateWorkflow(request, CreateContext(TestContext.Current.CancellationToken));
        response.ShouldNotBeNull();
        await repository.Received(1)
            .TerminateAsync(namespaceId, "wf", runId, "test", Arg.Any<CancellationToken>());
    }

    #endregion

    #region QueryWorkflow

    [Fact]
    public async Task QueryWorkflow_WhenWorkflowIdMissing_ThrowsInvalidArgument()
    {
        var service = CreateService();
        var request = new GrpcContracts.QueryWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = ""
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.QueryWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task QueryWorkflow_WhenRepositoryFails_ThrowsNotFound()
    {
        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetCurrentAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<DomainWorkflowExecution>(OdinErrors.WorkflowNotFound("wf")));

        var service = CreateService(repository);
        var request = new GrpcContracts.QueryWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = "wf"
        };

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.QueryWorkflow(request, CreateContext(TestContext.Current.CancellationToken)));
        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Fact]
    public async Task QueryWorkflow_Success()
    {
        var namespaceId = Guid.NewGuid();
        var execution = new DomainWorkflowExecution
        {
            NamespaceId = namespaceId,
            WorkflowId = "wf",
            RunId = Guid.NewGuid(),
            WorkflowType = "type",
            TaskQueue = "queue",
            WorkflowState = DomainWorkflowState.Running,
            StartedAt = DateTimeOffset.UtcNow,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            ShardId = 5
        };

        var repository = Substitute.For<IWorkflowExecutionRepository>();
        repository.GetCurrentAsync(namespaceId.ToString(), execution.WorkflowId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(execution));

        var service = CreateService(repository);
        var request = new GrpcContracts.QueryWorkflowRequest
        {
            NamespaceId = namespaceId.ToString(),
            WorkflowId = "wf"
        };

        var response = await service.QueryWorkflow(request, CreateContext(TestContext.Current.CancellationToken));
        response.Result.ShouldNotBeNull();
        response.Result.Fields["status"].StringValue.ShouldBe(execution.WorkflowState.ToString());
        response.Result.Fields["workflowType"].StringValue.ShouldBe("type");
    }

    #endregion

    private sealed class FakeServerCallContext : ServerCallContext
    {
        private readonly CancellationToken _cancellationToken;

        public FakeServerCallContext(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        protected override string MethodCore => "odintest";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => _cancellationToken;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } =
            new AuthContext("test", new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }
}
