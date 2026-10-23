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
    public async Task Atomic_json_inventory_updates_prevent_same_process_overselling()
    {
        var options = new JsonRepositoryOptions { DataDirectory = _directory };
        var repository = new JsonInventoryRepository(options);
        await repository.SaveAsync(new BranchInventoryItem(new("BR-A"), new("P-1"), 10));
        var serviceA = CreateService(repository);
        var serviceB = CreateService(repository);
        var removals = Enumerable.Range(0, 20).Select(async index =>
        {
            try
            {
                await (index % 2 == 0 ? serviceA : serviceB).RemoveStockAsync(new("BR-A"), new("P-1"), 1);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(removals);

        Assert.Equal(10, results.Count(result => result));
        Assert.Equal(0, (await repository.FindAsync(new("BR-A"), new("P-1")))!.Quantity);
    }

    private static InventoryService CreateService(JsonInventoryRepository repository) =>
        new(repository, new InventoryServiceOptions { LowStockThreshold = 2, LowStockRecipient = "inventory-alerts@example.test" });

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }
}
