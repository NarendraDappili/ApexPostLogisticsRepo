using ApexPost.Application.Pricing;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;

namespace ApexPost.Infrastructure.Pricing;

/// <summary>Loads configured shipping rates from the JSON seed/configuration file.</summary>
public sealed class JsonShippingRateProvider(JsonRepositoryOptions options) : IShippingRateProvider
{
    private readonly JsonFileStore<List<ShippingRateDocument>> _store = new(options, "shipping-rates.json");

    public async Task<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false);
        return documents.Select(document => new ShippingRate(
            document.ShipmentType,
            document.ServiceLevel,
            new ZoneCode(document.DestinationZoneCode),
            new Money(document.Charge),
            new ParcelWeight(document.MaximumWeightKilograms))).ToArray();
    }
}

internal sealed record ShippingRateDocument(
    ShipmentType ShipmentType,
    ShippingServiceLevel ServiceLevel,
    string DestinationZoneCode,
    decimal Charge,
    decimal MaximumWeightKilograms);
