using ApexPost.Domain.ValueObjects;
using ApexPost.Domain.Enums;

namespace ApexPost.Domain.Entities;

public sealed class Shipment : IEquatable<Shipment>
{
    public Shipment(
        ShipmentId id,
        BranchCode originBranchCode,
        ZoneCode destinationZoneCode,
        ParcelWeight weight,
        ParcelDimensions dimensions)
        : this(id, originBranchCode, destinationZoneCode, weight, dimensions, ShipmentType.Parcel, ShippingServiceLevel.Standard)
    {
    }

    public Shipment(
        ShipmentId id,
        BranchCode originBranchCode,
        ZoneCode destinationZoneCode,
        ParcelWeight weight,
        ParcelDimensions dimensions,
        ShipmentType shipmentType,
        ShippingServiceLevel serviceLevel)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(originBranchCode);
        ArgumentNullException.ThrowIfNull(destinationZoneCode);
        ArgumentNullException.ThrowIfNull(weight);
        ArgumentNullException.ThrowIfNull(dimensions);
        if (!Enum.IsDefined(shipmentType)) throw new ArgumentOutOfRangeException(nameof(shipmentType));
        if (!Enum.IsDefined(serviceLevel)) throw new ArgumentOutOfRangeException(nameof(serviceLevel));

        Id = id;
        OriginBranchCode = originBranchCode;
        DestinationZoneCode = destinationZoneCode;
        Weight = weight;
        Dimensions = dimensions;
        ShipmentType = shipmentType;
        ServiceLevel = serviceLevel;
    }

    public ShipmentId Id { get; }

    public BranchCode OriginBranchCode { get; }

    public ZoneCode DestinationZoneCode { get; }

    public ParcelWeight Weight { get; }

    public ParcelDimensions Dimensions { get; }
    public ShipmentType ShipmentType { get; }
    public ShippingServiceLevel ServiceLevel { get; }

    public bool Equals(Shipment? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is Shipment other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
