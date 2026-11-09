using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite.Query;

namespace ApexPost.Courier.Offline.Storage;

/// <summary>
/// The SQL++ text of every scan query. <c>_</c> is the default collection. The texts are constants: values from
/// callers are only ever passed as <c>$parameters</c>, never written into the query text.
/// </summary>
internal static class ScanQueries
{
    public const string TrackingNumberParameter = "trackingNumber";
    public const string SyncStatusParameter = "syncStatus";
    public const string CourierIdParameter = "courierId";

    /// <summary>Every scan field plus the document key, in the shape <see cref="ReadScans"/> expects.</summary>
    private const string ScanColumns =
        "SELECT META().id AS id, trackingNumber, scanType, courierId, location, scannedAtUtc, notes, syncStatus FROM _";

    private const string OldestFirst = " ORDER BY scannedAtUtc";

    // ListAll reads every document anyway, so it can afford META().id as an explicit tie-break.
    public const string ListAll = ScanColumns + OldestFirst + ", META().id";

    // These two are served by the (field, scannedAtUtc) indexes, which return rows already sorted. Adding META().id
    // would bring back a temporary sort. Scans with the same time come back in index order, which is stable.
    public const string ByTrackingNumber = ScanColumns + " WHERE trackingNumber = $" + TrackingNumberParameter + OldestFirst;

    public const string BySyncStatus = ScanColumns + " WHERE syncStatus = $" + SyncStatusParameter + OldestFirst;

    public const string CountBySyncStatus = "SELECT COUNT(*) AS scanCount FROM _ WHERE syncStatus = $" + SyncStatusParameter;

    public const string CountByScanTypeForCourier =
        "SELECT scanType, COUNT(*) AS scanCount FROM _ WHERE courierId = $" + CourierIdParameter + " GROUP BY scanType";

    public static IReadOnlyList<ShipmentScan> ReadScans(IQuery query)
    {
        using var results = query.Execute();
        return results.AllResults()
            .Select(row => ScanDocumentMapper.ToScan(row.GetString("id") ?? string.Empty, row))
            .ToList();
    }
}
