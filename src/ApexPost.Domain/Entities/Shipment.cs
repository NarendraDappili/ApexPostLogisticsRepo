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
        ShippingServiceLevel serviceLevel,
        CounterTransactionId? transactionId = null)
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
        TransactionId = transactionId;
    }

    // Used only when a stored shipment is materialized; the dimensions are loaded afterwards as an owned value.
    private Shipment(ShipmentId id, BranchCode originBranchCode, ZoneCode destinationZoneCode, ParcelWeight weight, ShipmentType shipmentType, ShippingServiceLevel serviceLevel, CounterTransactionId? transactionId)
    {
        Id = id;
        OriginBranchCode = originBranchCode;
        DestinationZoneCode = destinationZoneCode;
        Weight = weight;
        Dimensions = null!;
        ShipmentType = shipmentType;
        ServiceLevel = serviceLevel;
        TransactionId = transactionId;
    }

    public ShipmentId Id { get; }

    public BranchCode OriginBranchCode { get; }

    public ZoneCode DestinationZoneCode { get; }

    public ParcelWeight Weight { get; }

    public ParcelDimensions Dimensions { get; }
    public ShipmentType ShipmentType { get; }
    public ShippingServiceLevel ServiceLevel { get; }

    /// <summary>The counter transaction that bills this shipment; one shipment can be billed by one transaction only.</summary>
    public CounterTransactionId? TransactionId { get; }

    public bool Equals(Shipment? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is Shipment other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
