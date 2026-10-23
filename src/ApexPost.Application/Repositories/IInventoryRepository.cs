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
}
