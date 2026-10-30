using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

/// <summary>
/// Branch stock. Concurrent sales of the same item are detected when the unit of work commits
/// (optimistic concurrency), so a stale quantity can never be written back.
/// </summary>
public interface IInventoryRepository
{
    Task<BranchInventoryItem?> FindAsync(
        BranchCode branchCode,
        ProductId productId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<BranchInventoryItem>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default);

    void Update(BranchInventoryItem inventoryItem);
}
