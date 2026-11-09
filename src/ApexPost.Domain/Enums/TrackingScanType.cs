namespace ApexPost.Domain.Enums;

/// <summary>What a courier recorded when scanning a parcel; the same four types as the courier's offline store.</summary>
public enum TrackingScanType
{
    Pickup,
    InTransit,
    Delivered,
    Failed
}
