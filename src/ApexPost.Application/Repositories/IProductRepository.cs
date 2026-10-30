using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface IProductRepository
{
    Task<Product?> FindByIdAsync(ProductId id, CancellationToken cancellationToken = default);

    /// <summary>Returns the product catalogue for read-only presentation.</summary>
    Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken cancellationToken = default);
}
