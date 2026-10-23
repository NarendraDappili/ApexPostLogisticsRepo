using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Repositories;

public interface IProductRepository
{
    Task<Product?> FindByIdAsync(ProductId id, CancellationToken cancellationToken = default);

    Task SaveAsync(Product product, CancellationToken cancellationToken = default);
}
