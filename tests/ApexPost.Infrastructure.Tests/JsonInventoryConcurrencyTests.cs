using ApexPost.Application.Abstractions;
using ApexPost.Application.Inventory;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Repositories;

namespace ApexPost.Infrastructure.Tests;

public sealed class JsonInventoryConcurrencyTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ApexPost.Infrastructure.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Concurrent_json_stock_commits_never_lose_an_update_or_oversell()
    {
        var options = new JsonRepositoryOptions { DataDirectory = _directory };
        await new JsonInventoryRepository(options).SaveAsync(new BranchInventoryItem(new("BR-A"), new("P-1"), 10));

        // Each removal runs like a separate request: its own unit of work and repository.
        var removals = Enumerable.Range(0, 20).Select(async _ =>
        {
            var unitOfWork = new JsonUnitOfWork();
            var service = new InventoryService(new JsonInventoryRepository(options, unitOfWork), unitOfWork, new InventoryServiceOptions { LowStockThreshold = 2 });
            try
            {
                await service.RemoveStockAsync(new("BR-A"), new("P-1"), 1);
                return "sold";
            }
            catch (ConcurrencyConflictException)
            {
                return "conflict";
            }
            catch (InvalidOperationException)
            {
                return "out of stock";
            }
        });

        var results = await Task.WhenAll(removals);
        var stored = (await new JsonInventoryRepository(options).FindAsync(new("BR-A"), new("P-1")))!.Quantity;

        // Every successful sale is reflected exactly once; a stale read is rejected instead of overwriting.
        Assert.Equal(10 - results.Count(result => result == "sold"), stored);
        Assert.InRange(stored, 0, 10);
    }

    [Fact]
    public async Task A_stale_stock_read_is_rejected_at_commit()
    {
        var options = new JsonRepositoryOptions { DataDirectory = _directory };
        await new JsonInventoryRepository(options).SaveAsync(new BranchInventoryItem(new("BR-A"), new("P-1"), 1));
        var firstUnitOfWork = new JsonUnitOfWork();
        var secondUnitOfWork = new JsonUnitOfWork();
        var first = new JsonInventoryRepository(options, firstUnitOfWork);
        var second = new JsonInventoryRepository(options, secondUnitOfWork);

        // Two clerks read the last unit before either sale is committed.
        var firstRead = (await first.FindAsync(new("BR-A"), new("P-1")))!;
        var secondRead = (await second.FindAsync(new("BR-A"), new("P-1")))!;
        firstRead.RemoveStock(1);
        secondRead.RemoveStock(1);
        first.Update(firstRead);
        second.Update(secondRead);

        await firstUnitOfWork.SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondUnitOfWork.SaveChangesAsync());
        Assert.Equal(0, (await new JsonInventoryRepository(options).FindAsync(new("BR-A"), new("P-1")))!.Quantity);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }
}
