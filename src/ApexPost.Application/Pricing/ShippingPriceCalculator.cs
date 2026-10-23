using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Pricing;

/// <summary>
/// Selects one configured shipping rate and validates that the shipment is within that rate's supported weight.
/// </summary>
public static class ShippingPriceCalculator
{
    public static Money Calculate(ShippingPricingRequest request, IEnumerable<ShippingRate> rates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rates);

        var matchingRates = rates
            .Where(rate => rate is not null &&
                rate.ShipmentType == request.ShipmentType &&
                rate.ServiceLevel == request.ServiceLevel &&
                rate.DestinationZoneCode == request.Shipment.DestinationZoneCode)
            .ToList();

        return matchingRates.Count switch
        {
            0 => throw new InvalidOperationException("No configured shipping rate matches the requested shipment."),
            > 1 => throw new InvalidOperationException("More than one configured shipping rate matches the requested shipment."),
            _ => CalculateConfiguredRate(request, matchingRates[0])
        };
    }

    private static Money CalculateConfiguredRate(ShippingPricingRequest request, ShippingRate rate)
    {
        if (request.Shipment.Weight.Kilograms > rate.MaximumWeight.Kilograms)
        {
            throw new InvalidOperationException("The shipment exceeds the configured service-level weight limit.");
        }

        return rate.Charge;
    }
}
