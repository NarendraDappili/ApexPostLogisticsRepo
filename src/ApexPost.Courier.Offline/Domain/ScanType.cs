namespace ApexPost.Courier.Offline.Domain;

/// <summary>What happened to the parcel when the courier scanned it.</summary>
public enum ScanType
{
    Pickup,
    InTransit,
    Delivered,
    Failed,
}
