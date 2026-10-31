using ApexPost.Application.Catalogue;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Queries;

/// <summary>
/// Set-based catalogue writes with ExecuteUpdateAsync: one UPDATE statement, no entities loaded or tracked.
/// It bypasses SaveChanges and therefore the audit-column interceptor, so ModifiedAtUtc is set here.
/// </summary>
public sealed class EfCatalogueBulkUpdates(ApexPostDbContext db, TimeProvider timeProvider) : ICatalogueBulkUpdates
{
    public Task<int> RevisePricesAsync(string categoryCode, decimal factor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryCode);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factor);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return db.Products
            .Where(product => product.CategoryCode == categoryCode)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(product => product.UnitPrice.Amount, product => Math.Round(product.UnitPrice.Amount * factor, 2))
                .SetProperty(product => EF.Property<DateTime?>(product, ShadowColumns.ModifiedAtUtc), now),
                cancellationToken);
    }
}
