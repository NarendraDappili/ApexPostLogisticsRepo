using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface IInventoryRepository
{
    Task<BranchInventoryItem?> FindAsync(
        BranchCode branchCode,
        ProductId productId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(BranchInventoryItem inventoryItem, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<BranchInventoryItem>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a mutation against one inventory item as one repository operation.
    /// Implementations must not expose an intervening read/write window.
    /// </summary>
    Task<TResult> UpdateAsync<TResult>(
        BranchCode branchCode,
        ProductId productId,
        Func<BranchInventoryItem, TResult> update,
        CancellationToken cancellationToken = default);
}
