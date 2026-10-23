using ApexPost.Domain.Enums;

namespace ApexPost.Application.Shipping;

/// <summary>Raw shipment input validated into Domain value objects by the workflow.</summary>
public sealed record ShipmentCreationRequest(
    string ShipmentId,
    string OriginBranchCode,
    string DestinationZoneCode,
    ShipmentType ShipmentType,
    ShippingServiceLevel ServiceLevel,
    decimal WeightKilograms,
    decimal LengthCentimetres,
    decimal WidthCentimetres,
    decimal HeightCentimetres);
