namespace ApexPost.Application.Pricing;

/// <summary>Supplies configured shipping rates without exposing their storage mechanism.</summary>
public interface IShippingRateProvider
{
    Task<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken cancellationToken = default);
}
