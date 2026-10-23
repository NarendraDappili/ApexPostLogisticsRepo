using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Pricing;

/// <summary>
/// A configurable fixed charge and maximum supported weight for one shipment type, service level, and destination zone.
/// </summary>
public sealed record ShippingRate
{
    public ShippingRate(
        ShipmentType shipmentType,
        ShippingServiceLevel serviceLevel,
        ZoneCode destinationZoneCode,
        Money charge,
        ParcelWeight maximumWeight)
    {
        if (!Enum.IsDefined(shipmentType))
        {
            throw new ArgumentOutOfRangeException(nameof(shipmentType));
        }

        if (!Enum.IsDefined(serviceLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(serviceLevel));
        }

        ArgumentNullException.ThrowIfNull(destinationZoneCode);
        ArgumentNullException.ThrowIfNull(charge);
        ArgumentNullException.ThrowIfNull(maximumWeight);

        ShipmentType = shipmentType;
        ServiceLevel = serviceLevel;
        DestinationZoneCode = destinationZoneCode;
        Charge = charge;
        MaximumWeight = maximumWeight;
    }

    public ShipmentType ShipmentType { get; }

    public ShippingServiceLevel ServiceLevel { get; }

    public ZoneCode DestinationZoneCode { get; }

    public Money Charge { get; }

    public ParcelWeight MaximumWeight { get; }
}
