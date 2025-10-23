extern alias api;
extern alias grpc;
using Grpc.Core;
using Grpc.Net.Client;
using Shouldly;
using GetWorkflowRequest = api::Odin.ControlPlane.Grpc.GetWorkflowRequest;
using GrpcWorkflowServiceClient = api::Odin.ControlPlane.Grpc.WorkflowService.WorkflowServiceClient;
using QueryWorkflowRequest = api::Odin.ControlPlane.Grpc.QueryWorkflowRequest;
using StartWorkflowRequest = api::Odin.ControlPlane.Grpc.StartWorkflowRequest;
using TerminateWorkflowRequest = api::Odin.ControlPlane.Grpc.TerminateWorkflowRequest;

namespace Odin.Integration.Tests;

public sealed class WorkflowGrpcFixture : IAsyncLifetime
{
    private readonly GrpcServiceFactory _grpcFactory = new();
    private HttpClient? _httpClient;
    private GrpcChannel? _channel;

    public GrpcWorkflowServiceClient Client { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        _httpClient = _grpcFactory.CreateDefaultClient();
        _channel = GrpcChannel.ForAddress(_grpcFactory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpClient = _httpClient
        });
        Client = new GrpcWorkflowServiceClient(_channel);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _channel?.Dispose();
        _httpClient?.Dispose();
        _grpcFactory.Dispose();
        return ValueTask.CompletedTask;
    }
}

[CollectionDefinition("Workflow gRPC collection", DisableParallelization = true)]
public sealed class WorkflowGrpcCollection : ICollectionFixture<WorkflowGrpcFixture>;

[Collection("Workflow gRPC collection")]
public sealed class WorkflowGrpcServiceTests(WorkflowGrpcFixture fixture)
{
    private readonly GrpcWorkflowServiceClient _client = fixture.Client;

    [Fact]
    public async Task StartWorkflow_WithInvalidNamespace_ReturnsInvalidArgument()
    {
        var request = new StartWorkflowRequest
        {
            NamespaceId = "invalid-namespace",
            WorkflowType = "order-processing",
            TaskQueue = "integration-tests"
        };

        var ex = await Should.ThrowAsync<RpcException>(() =>
            _client.StartWorkflowAsync(request, cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task GetWorkflow_WhenMissing_ReturnsNotFound()
    {
        var request = new GetWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = "missing-workflow",
            RunId = Guid.NewGuid().ToString()
        };

        var ex = await Should.ThrowAsync<RpcException>(() =>
            _client.GetWorkflowAsync(request, cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Fact]
    public async Task QueryWorkflow_AfterStart_ReturnsRunningStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var namespaceId = Guid.NewGuid().ToString();

        var startResponse = await _client.StartWorkflowAsync(new StartWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowType = "order-processing",
            TaskQueue = "integration-tests"
        }, cancellationToken: cancellationToken).ResponseAsync;

        startResponse.ShouldNotBeNull();

        var queryResponse = await _client.QueryWorkflowAsync(new QueryWorkflowRequest
        {
            NamespaceId = namespaceId,
            WorkflowId = startResponse.WorkflowId,
            RunId = startResponse.RunId
        }, cancellationToken: cancellationToken).ResponseAsync;

        queryResponse.Result.ShouldNotBeNull();
        queryResponse.Result.Fields["status"].StringValue.ShouldBe("Running");
        queryResponse.Result.Fields["workflowType"].StringValue.ShouldBe("order-processing");
    }

    [Fact]
    public async Task TerminateWorkflow_WhenMissing_ReturnsNotFound()
    {
        var request = new TerminateWorkflowRequest
        {
            NamespaceId = Guid.NewGuid().ToString(),
            WorkflowId = "missing-workflow",
            RunId = Guid.NewGuid().ToString(),
            Reason = "integration-test"
        };

        var ex = await Should.ThrowAsync<RpcException>(() =>
            _client.TerminateWorkflowAsync(request, cancellationToken: TestContext.Current.CancellationToken).ResponseAsync);

        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }
}
