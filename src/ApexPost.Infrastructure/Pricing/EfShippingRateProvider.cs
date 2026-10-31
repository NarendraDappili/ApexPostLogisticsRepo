using ApexPost.Application.Pricing;
using ApexPost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Pricing;

/// <summary>Process-wide cache of the shipping rate table, shared by every request's provider.</summary>
public sealed class ShippingRateCache
{
    private volatile IReadOnlyCollection<ShippingRate>? _rates;

    internal IReadOnlyCollection<ShippingRate>? Rates
    {
        get => _rates;
        set => _rates = value;
    }
}

/// <summary>
/// Reads shipping rates from SQL Server once and then serves them from <see cref="ShippingRateCache"/>.
/// A cache hit completes synchronously, which is why the contract returns a ValueTask. An empty table is
/// not cached, so rates added later are picked up.
/// </summary>
public sealed class EfShippingRateProvider(ApexPostDbContext db, ShippingRateCache cache) : IShippingRateProvider
{
    public ValueTask<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken cancellationToken = default) =>
        cache.Rates is { } rates ? ValueTask.FromResult(rates) : new ValueTask<IReadOnlyCollection<ShippingRate>>(LoadAsync(cancellationToken));

    private async Task<IReadOnlyCollection<ShippingRate>> LoadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<ShippingRate> rates = await db.ShippingRates.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rates.Count > 0) cache.Rates = rates;
        return rates;
    }
}
