using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Shipping;

public sealed record ShipmentCreationResult(Shipment Shipment, ShipmentType ShipmentType, ShippingServiceLevel ServiceLevel, Money Price);
