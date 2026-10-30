namespace ApexPost.Application.Pricing;

/// <summary>
/// Supplies configured shipping rates without exposing their storage mechanism. Rates are read on every shipment
/// and checkout but change rarely, so implementations may answer from a cache synchronously through ValueTask.
/// </summary>
public interface IShippingRateProvider
{
    ValueTask<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken cancellationToken = default);
}
