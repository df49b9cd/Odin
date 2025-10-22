using Odin.Contracts;
using Odin.Persistence.Repositories;
using Shouldly;
using Xunit;

namespace Odin.Integration.Tests;

[Collection("PostgresIntegration")]
public sealed class NamespaceRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private readonly PostgresFixture _fixture = fixture;
    private NamespaceRepository? _repository;

    public async ValueTask InitializeAsync()
    {
        _fixture.EnsureDockerIsRunning();
        _repository ??= _fixture.CreateNamespaceRepository();
        await _fixture.ResetDatabaseAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CreateAsync_AndGetByName_ReturnsStoredNamespace()
    {
        var request = new CreateNamespaceRequest
        {
            NamespaceName = $"ns-{Guid.NewGuid():N}",
            Description = "Integration namespace",
            OwnerId = "owner-1",
            RetentionDays = 50,
            HistoryArchivalEnabled = true
        };

        var create = await Repository.CreateAsync(request, TestContext.Current.CancellationToken);
        create.IsSuccess.ShouldBeTrue(create.Error?.Message ?? "CreateAsync failed");

        var get = await Repository.GetByNameAsync(request.NamespaceName, TestContext.Current.CancellationToken);
        get.IsSuccess.ShouldBeTrue(get.Error?.Message ?? "GetByNameAsync failed");
        get.Value.NamespaceName.ShouldBe(request.NamespaceName);
        get.Value.RetentionDays.ShouldBe(50);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesNamespaceFields()
    {
        var name = $"ns-{Guid.NewGuid():N}";
        await Repository.CreateAsync(new CreateNamespaceRequest { NamespaceName = name }, TestContext.Current.CancellationToken);

        var update = await Repository.UpdateAsync(
            name,
            new UpdateNamespaceRequest
            {
                Description = "Updated",
                RetentionDays = 10,
                VisibilityArchivalEnabled = true
            },
            TestContext.Current.CancellationToken);

        update.IsSuccess.ShouldBeTrue(update.Error?.Message ?? "UpdateAsync failed");
        update.Value.Description.ShouldBe("Updated");
        update.Value.RetentionDays.ShouldBe(10);
        update.Value.VisibilityArchivalEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task ArchiveAsync_RemovesNamespaceFromList()
    {
        var name = $"ns-{Guid.NewGuid():N}";
        await Repository.CreateAsync(new CreateNamespaceRequest { NamespaceName = name }, TestContext.Current.CancellationToken);

        var archive = await Repository.ArchiveAsync(name, TestContext.Current.CancellationToken);
        archive.IsSuccess.ShouldBeTrue(archive.Error?.Message ?? "ArchiveAsync failed");

        var list = await Repository.ListAsync(pageSize: 10, cancellationToken: TestContext.Current.CancellationToken);
        list.IsSuccess.ShouldBeTrue(list.Error?.Message ?? "ListAsync failed");
        list.Value.Namespaces.ShouldNotContain(ns => ns.NamespaceName == name);
    }

    private NamespaceRepository Repository
        => _repository ?? throw new InvalidOperationException("Namespace repository was not initialized.");
}
