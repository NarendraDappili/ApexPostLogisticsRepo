using ApexPost.Application.Events;
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

    [Fact]
    public async Task Each_stock_change_commits_once_and_a_rejected_change_never_commits()
    {
        var unitOfWork = new SpyUnitOfWork();
        var service = CreateService(new InMemoryInventoryRepository(new BranchInventoryItem(new("BR-A"), new("P-1"), 2)), unitOfWork: unitOfWork);

        await service.AddStockAsync(new("BR-A"), new("P-1"), 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveStockAsync(new("BR-A"), new("P-1"), 10));

        Assert.Equal((1, 1), (unitOfWork.Attempts, unitOfWork.Commits));
    }

    [Fact]
    public async Task Stock_staged_for_a_sale_is_committed_by_the_caller_not_by_the_inventory_service()
    {
        var unitOfWork = new SpyUnitOfWork();
        var service = CreateService(new InMemoryInventoryRepository(new BranchInventoryItem(new("BR-A"), new("P-1"), 5)), unitOfWork: unitOfWork);

        var movement = await service.RemoveStockForSaleAsync(new("BR-A"), new("P-1"), 2);

        Assert.Equal((5, 3), (movement.Before, movement.After));
        Assert.Equal(0, unitOfWork.Attempts);
    }

    private static InventoryService CreateService(InMemoryInventoryRepository repository, INotificationSender? notifications = null, int threshold = 2, SpyUnitOfWork? unitOfWork = null) =>
        new(repository, unitOfWork ?? new SpyUnitOfWork(), new InventoryServiceOptions { LowStockThreshold = threshold },
            notifications is null ? null : new EventPublisher([new NotificationEventObserver(notifications, new NotificationRecipients { Inventory = "inventory-alerts@example.test" })]));

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

        public InMemoryInventoryRepository(params BranchInventoryItem[] items) =>
            _items = items.ToDictionary(item => (item.BranchCode.Value, item.ProductId.Value));

        public Task<BranchInventoryItem?> FindAsync(BranchCode branchCode, ProductId productId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _items.TryGetValue((branchCode.Value, productId.Value), out var item);
            return Task.FromResult(item);
        }
        public Task<IReadOnlyCollection<BranchInventoryItem>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<BranchInventoryItem>>(_items.Values.Where(item => item.BranchCode == branchCode).ToArray());

        public void Update(BranchInventoryItem inventoryItem) =>
            _items[(inventoryItem.BranchCode.Value, inventoryItem.ProductId.Value)] = inventoryItem;
    }
}
