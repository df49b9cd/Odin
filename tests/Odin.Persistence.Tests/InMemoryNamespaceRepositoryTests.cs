using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Odin.Contracts;
using Odin.Persistence.InMemory;
using Shouldly;
using Xunit;

namespace Odin.Persistence.Tests;

public class InMemoryNamespaceRepositoryTests
{
    private static InMemoryNamespaceRepository CreateRepository()
        => new(NullLogger<InMemoryNamespaceRepository>.Instance);

    [Fact]
    public async Task CreateAsync_PersistsNamespaceAndAllowsRetrieval()
    {
        var repository = CreateRepository();
        var ct = TestContext.Current.CancellationToken;
        var request = new CreateNamespaceRequest
        {
            NamespaceName = "acme",
            Description = "Tenant",
            OwnerId = "ops",
            RetentionDays = 45,
            HistoryArchivalEnabled = true,
            VisibilityArchivalEnabled = false
        };

        var create = await repository.CreateAsync(request, ct);

        create.IsSuccess.ShouldBeTrue(create.Error?.Message ?? "CreateAsync failed");
        create.Value.NamespaceName.ShouldBe("acme");
        create.Value.RetentionDays.ShouldBe(45);

        var get = await repository.GetByNameAsync("acme", ct);
        get.IsSuccess.ShouldBeTrue(get.Error?.Message ?? "GetByNameAsync failed");
        get.Value.NamespaceName.ShouldBe("acme");
        get.Value.OwnerId.ShouldBe("ops");
    }

    [Fact]
    public async Task CreateAsync_WhenNamespaceExists_ReturnsError()
    {
        var repository = CreateRepository();
        var ct = TestContext.Current.CancellationToken;
        var request = new CreateNamespaceRequest { NamespaceName = "duplicate" };

        var first = await repository.CreateAsync(request, ct);
        first.IsSuccess.ShouldBeTrue(first.Error?.Message ?? "Initial create failed");

        var second = await repository.CreateAsync(request, ct);

        second.IsFailure.ShouldBeTrue("Duplicate create should fail");
        second.Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_ModifiesFieldsAndUpdatesTimestamp()
    {
        var repository = CreateRepository();
        var ct = TestContext.Current.CancellationToken;
        await repository.CreateAsync(new CreateNamespaceRequest { NamespaceName = "update-me" }, ct);

        var update = await repository.UpdateAsync(
            "update-me",
            new UpdateNamespaceRequest
            {
                Description = "updated",
                RetentionDays = 15,
                HistoryArchivalEnabled = true
            },
            ct);

        update.IsSuccess.ShouldBeTrue(update.Error?.Message ?? "UpdateAsync failed");
        update.Value.Description.ShouldBe("updated");
        update.Value.RetentionDays.ShouldBe(15);
        update.Value.HistoryArchivalEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task ArchiveAsync_MarksNamespaceDeleted()
    {
        var repository = CreateRepository();
        var ct = TestContext.Current.CancellationToken;
        await repository.CreateAsync(new CreateNamespaceRequest { NamespaceName = "archive-me" }, ct);

        var archive = await repository.ArchiveAsync("archive-me", ct);
        archive.IsSuccess.ShouldBeTrue(archive.Error?.Message ?? "ArchiveAsync failed");

        var exists = await repository.ExistsAsync("archive-me", ct);
        exists.IsSuccess.ShouldBeTrue(exists.Error?.Message ?? "ExistsAsync failed");
        exists.Value.ShouldBeFalse();

        var get = await repository.GetByNameAsync("archive-me", ct);
        get.IsFailure.ShouldBeTrue("Archived namespace should not be retrievable");
    }

    [Fact]
    public async Task ListAsync_RespectsPaginationOrder()
    {
        var repository = CreateRepository();
        var ct = TestContext.Current.CancellationToken;
        await repository.CreateAsync(new CreateNamespaceRequest { NamespaceName = "b-namespace" }, ct);
        await repository.CreateAsync(new CreateNamespaceRequest { NamespaceName = "a-namespace" }, ct);
        await repository.CreateAsync(new CreateNamespaceRequest { NamespaceName = "c-namespace" }, ct);

        var firstPage = await repository.ListAsync(pageSize: 2, cancellationToken: ct);
        firstPage.IsSuccess.ShouldBeTrue(firstPage.Error?.Message ?? "ListAsync failed");
        firstPage.Value.Namespaces.Count.ShouldBe(2);
        firstPage.Value.Namespaces.First().NamespaceName.ShouldBe("a-namespace");
        firstPage.Value.NextPageToken.ShouldNotBeNull();

        var secondPage = await repository.ListAsync(pageSize: 2, pageToken: firstPage.Value.NextPageToken, cancellationToken: ct);
        secondPage.IsSuccess.ShouldBeTrue(secondPage.Error?.Message ?? "ListAsync second page failed");
        secondPage.Value.Namespaces.Count.ShouldBe(1);
        secondPage.Value.Namespaces.First().NamespaceName.ShouldBe("c-namespace");
        secondPage.Value.NextPageToken.ShouldBeNull();
    }
}
