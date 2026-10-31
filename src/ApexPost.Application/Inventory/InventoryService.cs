using ApexPost.Application.Abstractions;
using ApexPost.Application.Events;
using ApexPost.Application.Repositories;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Inventory;

/// <summary>Coordinates branch-specific stock changes while Domain inventory guards its invariant.</summary>
public sealed class InventoryService
{
    private readonly IInventoryRepository _inventory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventPublisher? _events;
    private readonly InventoryServiceOptions _options;

    public InventoryService(IInventoryRepository inventory, IUnitOfWork unitOfWork, InventoryServiceOptions options, IEventPublisher? events = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.LowStockThreshold);
        _inventory = inventory;
        _unitOfWork = unitOfWork;
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
        CommitAndAlertAsync(branchCode, productId, CheckedPositiveQuantity(quantity), cancellationToken);

    public Task<InventoryStockLevel> RemoveStockAsync(BranchCode branchCode, ProductId productId, int quantity, CancellationToken cancellationToken = default) =>
        CommitAndAlertAsync(branchCode, productId, -CheckedPositiveQuantity(quantity), cancellationToken);

    public Task<InventoryStockLevel> AdjustStockAsync(BranchCode branchCode, ProductId productId, int quantityDelta, CancellationToken cancellationToken = default)
    {
        if (quantityDelta == 0) throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Stock adjustment cannot be zero.");
        return CommitAndAlertAsync(branchCode, productId, quantityDelta, cancellationToken);
    }

    /// <summary>
    /// Stages a stock removal for a sale. Nothing is committed here: the checkout commits the stock and the sale
    /// in one unit of work, then raises <see cref="RaiseLowStockAlertAsync"/> for the committed movement.
    /// </summary>
    public Task<StockMovement> RemoveStockForSaleAsync(BranchCode branchCode, ProductId productId, int quantity, CancellationToken cancellationToken = default) =>
        StageChangeAsync(branchCode, productId, -CheckedPositiveQuantity(quantity), cancellationToken);

    /// <summary>Stages the return of sold stock for a void or refund; the reversal commits it with the new status.</summary>
    public Task<StockMovement> ReturnStockForReversalAsync(BranchCode branchCode, ProductId productId, int quantity, CancellationToken cancellationToken = default) =>
        StageChangeAsync(branchCode, productId, CheckedPositiveQuantity(quantity), cancellationToken);

    /// <summary>Raises a StockLow event when this movement crossed the configured low-stock threshold.</summary>
    public Task RaiseLowStockAlertAsync(StockMovement movement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movement);
        var threshold = _options.LowStockThreshold;
        return movement.Before > threshold && movement.After <= threshold && _events is not null
            ? _events.PublishAsync(new StockLow(movement.BranchCode, movement.ProductId, movement.After, threshold), cancellationToken)
            : Task.CompletedTask;
    }

    private async Task<InventoryStockLevel> CommitAndAlertAsync(BranchCode branchCode, ProductId productId, int quantityDelta, CancellationToken cancellationToken)
    {
        var movement = await StageChangeAsync(branchCode, productId, quantityDelta, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        // The stock change is committed; the alert must not be lost if the client disconnects now.
        await RaiseLowStockAlertAsync(movement, CancellationToken.None).ConfigureAwait(false);
        return new InventoryStockLevel(movement.BranchCode, movement.ProductId, movement.After);
    }

    private async Task<StockMovement> StageChangeAsync(BranchCode branchCode, ProductId productId, int quantityDelta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(productId);
        if (quantityDelta == 0) throw new ArgumentOutOfRangeException(nameof(quantityDelta), "Stock adjustment cannot be zero.");

        var item = await _inventory.FindAsync(branchCode, productId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No inventory item exists for the branch and product.");
        var before = item.Quantity;
        // The domain rejects a removal below zero; a concurrent change is caught when the unit of work commits.
        if (quantityDelta > 0) item.AddStock(quantityDelta); else item.RemoveStock(checked(-quantityDelta));
        _inventory.Update(item);
        return new StockMovement(item.BranchCode, item.ProductId, before, item.Quantity);
    }

    private static int CheckedPositiveQuantity(int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        return quantity;
    }
}

public sealed record StockMovement(BranchCode BranchCode, ProductId ProductId, int Before, int After);
