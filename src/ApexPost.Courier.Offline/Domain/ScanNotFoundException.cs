namespace ApexPost.Courier.Offline.Domain;

/// <summary>A scan that should exist on the device was not found, for example because it was deleted meanwhile.</summary>
public sealed class ScanNotFoundException(ScanId scanId)
    : Exception($"Scan {scanId} was not found on this device.")
{
    public ScanId ScanId { get; } = scanId;
}
