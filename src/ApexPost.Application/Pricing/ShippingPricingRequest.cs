using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;

namespace ApexPost.Application.Pricing;

/// <summary>
/// Supplies the shipment and requested service attributes required to select a configured shipping rate.
/// </summary>
public sealed record ShippingPricingRequest
{
    public ShippingPricingRequest(Shipment shipment, ShipmentType shipmentType, ShippingServiceLevel serviceLevel)
    {
        ArgumentNullException.ThrowIfNull(shipment);

        if (!Enum.IsDefined(shipmentType))
        {
            throw new ArgumentOutOfRangeException(nameof(shipmentType));
        }

        if (!Enum.IsDefined(serviceLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(serviceLevel));
        }

        Shipment = shipment;
        ShipmentType = shipmentType;
        ServiceLevel = serviceLevel;
    }

    public Shipment Shipment { get; }

    public ShipmentType ShipmentType { get; }

    public ShippingServiceLevel ServiceLevel { get; }
}
