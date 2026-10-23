using ApexPost.Application.Inventory;
using ApexPost.Application.Notifications;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Inventory;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task Available_stock_is_branch_specific()
    {
        var repository = new InMemoryInventoryRepository(
            new BranchInventoryItem(new("BR-A"), new("P-1"), 8),
            new BranchInventoryItem(new("BR-B"), new("P-1"), 3));
        var service = CreateService(repository);

        Assert.Equal(8, (await service.GetAvailableStockAsync(new("BR-A"), new("P-1"))).Quantity);
        Assert.Equal(3, (await service.GetAvailableStockAsync(new("BR-B"), new("P-1"))).Quantity);
    }

    [Fact]
    public async Task Insufficient_stock_fails_without_changing_stock()
    {
        var repository = new InMemoryInventoryRepository(new BranchInventoryItem(new("BR-A"), new("P-1"), 2));
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveStockAsync(new("BR-A"), new("P-1"), 3));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustStockAsync(new("BR-A"), new("P-1"), -3));
        Assert.Equal(2, (await service.GetAvailableStockAsync(new("BR-A"), new("P-1"))).Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_add_or_remove_quantities_fail(int quantity)
    {
        var service = CreateService(new InMemoryInventoryRepository(new BranchInventoryItem(new("BR-A"), new("P-1"), 2)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.AddStockAsync(new("BR-A"), new("P-1"), quantity));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RemoveStockAsync(new("BR-A"), new("P-1"), quantity));
        if (quantity == 0)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.AdjustStockAsync(new("BR-A"), new("P-1"), quantity));
        }
    }

    [Fact]
    public async Task Positive_and_negative_adjustments_change_only_the_requested_branch()
    {
        var repository = new InMemoryInventoryRepository(
            new BranchInventoryItem(new("BR-A"), new("P-1"), 5),
            new BranchInventoryItem(new("BR-B"), new("P-1"), 5));
        var service = CreateService(repository);

        Assert.Equal(9, (await service.AdjustStockAsync(new("BR-A"), new("P-1"), 4)).Quantity);
        Assert.Equal(7, (await service.AdjustStockAsync(new("BR-A"), new("P-1"), -2)).Quantity);
        Assert.Equal(5, (await service.GetAvailableStockAsync(new("BR-B"), new("P-1"))).Quantity);
    }

    [Fact]
    public async Task Low_stock_notification_is_sent_once_when_stock_crosses_threshold()
    {
        var notifications = new RecordingNotificationSender();
        var service = CreateService(new InMemoryInventoryRepository(new BranchInventoryItem(new("BR-A"), new("P-1"), 6)), notifications, threshold: 5);

        await service.RemoveStockAsync(new("BR-A"), new("P-1"), 1);
        await service.RemoveStockAsync(new("BR-A"), new("P-1"), 1);
        await service.AddStockAsync(new("BR-A"), new("P-1"), 2);
        await service.RemoveStockAsync(new("BR-A"), new("P-1"), 1);

        Assert.Equal(2, notifications.Messages.Count);
        Assert.All(notifications.Messages, message => Assert.Equal("inventory-alerts@example.test", message.Recipient));
    }

    [Fact]
    public async Task Concurrent_removals_do_not_oversell_when_repository_update_is_atomic()
    {
        var repository = new InMemoryInventoryRepository(new BranchInventoryItem(new("BR-A"), new("P-1"), 10));
        var service = CreateService(repository);
        var removals = Enumerable.Range(0, 20).Select(async _ =>
        {
            try
            {
                await service.RemoveStockAsync(new("BR-A"), new("P-1"), 1);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(removals);

        Assert.Equal(10, results.Count(result => result));
        Assert.Equal(0, (await service.GetAvailableStockAsync(new("BR-A"), new("P-1"))).Quantity);
    }

    private static InventoryService CreateService(InMemoryInventoryRepository repository, INotificationSender? notifications = null, int threshold = 2) =>
        new(repository, new InventoryServiceOptions { LowStockThreshold = threshold, LowStockRecipient = "inventory-alerts@example.test" }, notifications);

    private sealed class RecordingNotificationSender : INotificationSender
    {
        public List<NotificationMessage> Messages { get; } = [];
        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryInventoryRepository : IInventoryRepository
    {
        private readonly Dictionary<(string Branch, string Product), BranchInventoryItem> _items;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public InMemoryInventoryRepository(params BranchInventoryItem[] items) =>
            _items = items.ToDictionary(item => (item.BranchCode.Value, item.ProductId.Value));

        public Task<BranchInventoryItem?> FindAsync(BranchCode branchCode, ProductId productId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _items.TryGetValue((branchCode.Value, productId.Value), out var item);
            return Task.FromResult(item);
        }

        public Task SaveAsync(BranchInventoryItem inventoryItem, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _items[(inventoryItem.BranchCode.Value, inventoryItem.ProductId.Value)] = inventoryItem;
            return Task.CompletedTask;
        }

        public async Task<TResult> UpdateAsync<TResult>(BranchCode branchCode, ProductId productId, Func<BranchInventoryItem, TResult> update, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!_items.TryGetValue((branchCode.Value, productId.Value), out var item)) throw new InvalidOperationException("No inventory item exists for the branch and product.");
                return update(item);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
