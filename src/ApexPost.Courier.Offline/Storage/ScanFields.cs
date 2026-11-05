namespace ApexPost.Courier.Offline.Storage;

/// <summary>The JSON property names of a shipment scan document, shared by the mapper, queries and indexes.</summary>
internal static class ScanFields
{
    public const string TrackingNumber = "trackingNumber";
    public const string ScanType = "scanType";
    public const string CourierId = "courierId";
    public const string Location = "location";
    public const string ScannedAtUtc = "scannedAtUtc";
    public const string Notes = "notes";
    public const string SyncStatus = "syncStatus";
}
