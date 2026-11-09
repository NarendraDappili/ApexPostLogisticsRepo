namespace ApexPost.Domain.Enums;

/// <summary>
/// Where a tracked shipment is on its way to the recipient. The only allowed moves are
/// Created → PickedUp → InTransit → OutForDelivery → Delivered or Failed (see <see cref="Entities.TrackingStatusRules"/>).
/// </summary>
public enum TrackingStatus
{
    Created,
    PickedUp,
    InTransit,
    OutForDelivery,
    Delivered,
    Failed
}
