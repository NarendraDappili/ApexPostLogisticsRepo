using ApexPost.Application.Events;
using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Inventory;

/// <summary>Coordinates branch-specific stock changes while Domain inventory guards its invariant.</summary>
public sealed class InventoryService
{
    private readonly IInventoryRepository _inventory;
    private readonly IEventPublisher? _events;
    private readonly InventoryServiceOptions _options;

    public InventoryService(IInventoryRepository inventory, InventoryServiceOptions options, IEventPublisher? events = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.LowStockThreshold);
        _inventory = inventory;
        _options = options;
        _events = events;
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
        ChangeAndAlertAsync(branchCode, productId, CheckedPositiveQuantity(quantity), cancellationToken);

    public Task<InventoryStockLevel> RemoveStockAsync(BranchCode branchCode, ProductId productId, int quantity, CancellationToken cancellationToken = default) =>
        ChangeAndAlertAsync(branchCode, productId, -CheckedPositiveQuantity(quantity), cancellationToken);

    public Task<InventoryStockLevel> AdjustStockAsync(BranchCode branchCode, ProductId productId, int quantityDelta, CancellationToken cancellationToken = default)
    {
        if (quantityDelta == 0) throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Stock adjustment cannot be zero.");
        return ChangeAndAlertAsync(branchCode, productId, quantityDelta, cancellationToken);
    }

    /// <summary>
    /// Removes stock for a sale without raising alerts. The checkout raises <see cref="RaiseLowStockAlertAsync"/>
    /// only after the sale is persisted, so an alert failure can never strand stock that compensation must return.
    /// </summary>
    public Task<StockMovement> RemoveStockForSaleAsync(BranchCode branchCode, ProductId productId, int quantity, CancellationToken cancellationToken = default) =>
        ChangeStockAsync(branchCode, productId, -CheckedPositiveQuantity(quantity), cancellationToken);

    /// <summary>Raises a StockLow event when this movement crossed the configured low-stock threshold.</summary>
    public Task RaiseLowStockAlertAsync(StockMovement movement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);
        var threshold = _options.LowStockThreshold;
        return movement.Before > threshold && movement.After <= threshold && _events is not null
            ? _events.PublishAsync(new StockLow(movement.BranchCode, movement.ProductId, movement.After, threshold), cancellationToken)
            : Task.CompletedTask;
    }

    private async Task<InventoryStockLevel> ChangeAndAlertAsync(BranchCode branchCode, ProductId productId, int quantityDelta, CancellationToken cancellationToken)
    {
        var movement = await ChangeStockAsync(branchCode, productId, quantityDelta, cancellationToken).ConfigureAwait(false);
        await RaiseLowStockAlertAsync(movement, cancellationToken).ConfigureAwait(false);
        return new InventoryStockLevel(movement.BranchCode, movement.ProductId, movement.After);
    }

    private Task<StockMovement> ChangeStockAsync(BranchCode branchCode, ProductId productId, int quantityDelta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(productId);
        if (quantityDelta == 0) throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Stock adjustment cannot be zero.");

        return _inventory.UpdateAsync(branchCode, productId, item =>
        {
            var before = item.Quantity;
            if (quantityDelta > 0) item.AddStock(quantityDelta); else item.RemoveStock(checked(-quantityDelta));
            return new StockMovement(item.BranchCode, item.ProductId, before, item.Quantity);
        }, cancellationToken);
    }

    private static int CheckedPositiveQuantity(int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        return quantity;
    }
}

public sealed record StockMovement(BranchCode BranchCode, ProductId ProductId, int Before, int After);
