using ApexPost.Application.Pricing;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;

namespace ApexPost.Infrastructure.Pricing;

/// <summary>
/// Loads configured shipping rates from the JSON seed/configuration file and keeps them in memory.
/// Every shipment and every basket with a shipment reads the full rate table, but the table only changes when the
/// data is reseeded, so after the first load the call completes synchronously: no file lock, no disk read, no
/// deserialization and no Task allocation (the ValueTask wraps the cached array).
/// </summary>
public sealed class JsonShippingRateProvider(JsonRepositoryOptions options) : IShippingRateProvider
{
    private readonly JsonFileStore<List<ShippingRateDocument>> _store = new(options, "shipping-rates.json");
    private volatile IReadOnlyCollection<ShippingRate>? _cache;

    public ValueTask<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _cache is { } cached
            ? ValueTask.FromResult(cached)
            : new ValueTask<IReadOnlyCollection<ShippingRate>>(LoadAsync(cancellationToken));
    }

    private async Task<IReadOnlyCollection<ShippingRate>> LoadAsync(CancellationToken cancellationToken)
    {
        var documents = await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false);
        var rates = documents.Select(document => new ShippingRate(
            document.ShipmentType,
            document.ServiceLevel,
            new ZoneCode(document.DestinationZoneCode),
            new Money(document.Charge),
            new ParcelWeight(document.MaximumWeightKilograms))).ToArray();
        // An empty table is not cached, so rates seeded after startup are still picked up. Concurrent first
        // loads may both read the file; they produce identical tables, so the last write wins harmlessly.
        if (rates.Length > 0) _cache = rates;
        return rates;
    }
}

internal sealed record ShippingRateDocument(
    ShipmentType ShipmentType,
    ShippingServiceLevel ServiceLevel,
    string DestinationZoneCode,
    decimal Charge,
    decimal MaximumWeightKilograms);
