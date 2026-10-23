using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class Shipment : IEquatable<Shipment>
{
    public Shipment(
        ShipmentId id,
        BranchCode originBranchCode,
        ZoneCode destinationZoneCode,
        ParcelWeight weight,
        ParcelDimensions dimensions)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(originBranchCode);
        ArgumentNullException.ThrowIfNull(destinationZoneCode);
        ArgumentNullException.ThrowIfNull(weight);
        ArgumentNullException.ThrowIfNull(dimensions);

        Id = id;
        OriginBranchCode = originBranchCode;
        DestinationZoneCode = destinationZoneCode;
        Weight = weight;
        Dimensions = dimensions;
    }

    public ShipmentId Id { get; }

    public BranchCode OriginBranchCode { get; }

    public ZoneCode DestinationZoneCode { get; }

    public ParcelWeight Weight { get; }

    public ParcelDimensions Dimensions { get; }

    public bool Equals(Shipment? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is Shipment other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
