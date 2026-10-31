using ApexPost.Application.Abstractions;
using ApexPost.Application.Repositories;
using ApexPost.Application.Validation;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Inventory;

public sealed record StockAdjustmentRequest(
    [property: NotBlank(48)] string BranchCode,
    [property: NotBlank(48)] string ProductId,
    int Delta,
    [property: NotBlank(StockAdjustment.MaxReasonLength)] string Reason);

/// <summary>
/// Applies one stock correction in the database: the stock row changes and its history row is written together,
/// immediately, inside the caller's transaction (usp_AdjustStock).
/// </summary>
public interface IStockAdjustments
{
    /// <returns>The branch's new quantity of the product.</returns>
    Task<int> ApplyAsync(StockAdjustment adjustment, Sku sku, CancellationToken cancellationToken = default);
}

/// <summary>
/// A Branch Manager corrects stock (a count, damage, a delivery). A product the branch has never stocked first
/// gets a stock row through the unit of work, then the correction runs; both happen in one explicit transaction.
/// </summary>
public sealed class StockAdjustmentService
{
    private readonly IStaffMemberRepository _staff;
    private readonly IProductRepository _products;
    private readonly IInventoryRepository _inventory;
    private readonly IStockAdjustments _adjustments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly InventoryService _inventoryService;

    public StockAdjustmentService(IStaffMemberRepository staff, IProductRepository products, IInventoryRepository inventory, IStockAdjustments adjustments, IUnitOfWork unitOfWork, InventoryService inventoryService)
    {
        _staff = staff ?? throw new ArgumentNullException(nameof(staff));
        _products = products ?? throw new ArgumentNullException(nameof(products));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _adjustments = adjustments ?? throw new ArgumentNullException(nameof(adjustments));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
    }

    public async Task<InventoryStockLevel> AdjustAsync(StockAdjustmentRequest request, StaffId managerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(managerId);
        RequestValidator.Validate(request);
        var branchCode = new BranchCode(request.BranchCode);
        var productId = new ProductId(request.ProductId);
        // The domain record validates the change (non-zero, a reason of at most 200 characters) before any database work.
        var adjustment = new StockAdjustment(branchCode, productId, request.Delta, request.Reason, managerId);

        var manager = await _staff.FindByIdAsync(managerId, cancellationToken).ConfigureAwait(false);
        if (manager is null || !manager.IsActive || manager.Role != StaffRole.BranchManager || manager.AssignedBranchCode != branchCode)
        {
            throw new InvalidOperationException("A Branch Manager assigned to the branch must make stock adjustments.");
        }

        var product = await _products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The product was not found.");

        var newQuantity = await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (await _inventory.FindAsync(branchCode, productId, token).ConfigureAwait(false) is null)
            {
                if (request.Delta < 0) throw new InvalidOperationException("The branch does not stock this product.");
                // First delivery of this product to the branch: create its stock row, then let the procedure add the units.
                _inventory.Add(new BranchInventoryItem(branchCode, productId));
                await _unitOfWork.SaveChangesAsync(token).ConfigureAwait(false);
            }

            return await _adjustments.ApplyAsync(adjustment, product.Sku, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        // Committed: raise the low-stock alert without the request token so a disconnect cannot drop it.
        await _inventoryService.RaiseLowStockAlertAsync(new StockMovement(branchCode, productId, newQuantity - request.Delta, newQuantity), CancellationToken.None).ConfigureAwait(false);
        return new InventoryStockLevel(branchCode, productId, newQuantity);
    }
}
