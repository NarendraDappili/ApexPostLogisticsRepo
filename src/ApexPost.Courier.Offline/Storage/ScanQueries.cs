using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite.Query;

namespace ApexPost.Courier.Offline.Storage;

/// <summary>
/// The SQL++ text of every scan query. <c>_</c> is the default collection. Values from callers are only ever
/// passed as <c>$parameters</c>, never written into the query text.
/// </summary>
internal static class ScanQueries
{
    /// <summary>Every scan field plus the document key, in the shape <see cref="ReadScans"/> expects.</summary>
    private const string ScanColumns =
        "SELECT META().id AS id, trackingNumber, scanType, courierId, location, scannedAtUtc, notes, syncStatus FROM _";

    public const string ListAll = ScanColumns + " ORDER BY scannedAtUtc, META().id";

    public static IReadOnlyList<ShipmentScan> ReadScans(IQuery query)
    {
        using var results = query.Execute();
        return results.AllResults()
            .Select(row => ScanDocumentMapper.ToScan(row.GetString("id") ?? string.Empty, row))
            .ToList();
    }
}
