using ApexPost.Application.Validation;
using ApexPost.Domain.Enums;

namespace ApexPost.Application.Shipping;

/// <summary>
/// Raw shipment input validated into Domain value objects by the workflow. The optional transaction ID links the
/// shipment to the counter transaction that will bill it; the price is always calculated on the server.
/// </summary>
public sealed record ShipmentCreationRequest(
    [property: NotBlank(48)] string ShipmentId,
    [property: NotBlank(48)] string OriginBranchCode,
    [property: NotBlank(48)] string DestinationZoneCode,
    ShipmentType ShipmentType,
    ShippingServiceLevel ServiceLevel,
    decimal WeightKilograms,
    decimal LengthCentimetres,
    decimal WidthCentimetres,
    decimal HeightCentimetres,
    string? TransactionId = null);
