using ApexPost.Application.Notifications;
using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Inventory;

/// <summary>Coordinates branch-specific stock changes while Domain inventory guards its invariant.</summary>
public sealed class InventoryService
{
    private readonly IInventoryRepository _inventory;
    private readonly INotificationSender? _notifications;
    private readonly InventoryServiceOptions _options;

    public InventoryService(IInventoryRepository inventory, InventoryServiceOptions options, INotificationSender? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.LowStockThreshold);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LowStockRecipient);
        _inventory = inventory;
        _options = options;
        _notifications = notifications;
    }

    public async Task<InventoryStockLevel> GetAvailableStockAsync(BranchCode branchCode, ProductId productId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(productId);
        var item = await _inventory.FindAsync(branchCode, productId, cancellationToken).ConfigureAwait(false);
        return item is null
            ? throw new InvalidOperationException("No inventory item exists for the branch and product.")
            : new InventoryStockLevel(item.BranchCode, item.ProductId, item.Quantity);
    }

    public Task<InventoryStockLevel> AddStockAsync(BranchCode branchCode, ProductId productId, int quantity, CancellationToken cancellationToken = default) =>
        ChangeStockAsync(branchCode, productId, CheckedPositiveQuantity(quantity), cancellationToken);

    public Task<InventoryStockLevel> RemoveStockAsync(BranchCode branchCode, ProductId productId, int quantity, CancellationToken cancellationToken = default) =>
        ChangeStockAsync(branchCode, productId, -CheckedPositiveQuantity(quantity), cancellationToken);

    public Task<InventoryStockLevel> AdjustStockAsync(BranchCode branchCode, ProductId productId, int quantityDelta, CancellationToken cancellationToken = default)
    {
        if (quantityDelta == 0) throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Stock adjustment cannot be zero.");
        return ChangeStockAsync(branchCode, productId, quantityDelta, cancellationToken);
    }

    private async Task<InventoryStockLevel> ChangeStockAsync(BranchCode branchCode, ProductId productId, int quantityDelta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(productId);
        if (quantityDelta == 0) throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Stock adjustment cannot be zero.");

        var change = await _inventory.UpdateAsync(branchCode, productId, item =>
        {
            var before = item.Quantity;
            if (quantityDelta > 0) item.AddStock(quantityDelta); else item.RemoveStock(checked(-quantityDelta));
            return new StockChange(before, item.Quantity, item.BranchCode, item.ProductId);
        }, cancellationToken).ConfigureAwait(false);

        if (change.Before > _options.LowStockThreshold && change.After <= _options.LowStockThreshold && _notifications is not null)
        {
            await _notifications.SendAsync(
                new NotificationMessage(
                    _options.LowStockRecipient,
                    "ApexPost low-stock alert",
                    $"{change.ProductId.Value} at {change.BranchCode.Value} has {change.After} units available."),
                cancellationToken).ConfigureAwait(false);
        }

        return new InventoryStockLevel(change.BranchCode, change.ProductId, change.After);
    }

    private static int CheckedPositiveQuantity(int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        return quantity;
    }

    private sealed record StockChange(int Before, int After, BranchCode BranchCode, ProductId ProductId);
}
