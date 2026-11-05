using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.Courier.Offline.Storage;

/// <summary>
/// Stores shipment scans as JSON documents in the courier database's default collection. Reads and writes by ID
/// are key-value operations on the document key (<c>scan::{Guid}</c>); listing uses SQL++.
/// </summary>
public sealed class CouchbaseScanRepository : IScanRepository
{
    private readonly CourierDatabase _database;
    private readonly ILogger<CouchbaseScanRepository> _logger;

    public CouchbaseScanRepository(CourierDatabase database, ILogger<CouchbaseScanRepository>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _logger = logger ?? NullLogger<CouchbaseScanRepository>.Instance;
    }

    public void Create(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (scan.SyncStatus != SyncStatus.Pending)
        {
            throw new ArgumentException("A new scan is saved locally first, as Pending.", nameof(scan));
        }

        using var document = ScanDocumentMapper.ToNewDocument(scan);
        // A brand-new document conflicts with any stored document of the same ID, so FailOnConflict makes the
        // duplicate check and the insert one step: nothing is overwritten.
        if (!_database.Scans.Save(document, ConcurrencyControl.FailOnConflict))
        {
            throw new InvalidOperationException($"Scan {scan.Id} already exists on this device.");
        }

        _logger.LogDebug("Saved scan {ScanId} locally as Pending", scan.Id);
    }

    public ShipmentScan? GetById(ScanId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        using var document = _database.Scans.GetDocument(id.Value);
        return document is null ? null : ScanDocumentMapper.ToScan(document.Id, document);
    }

    public void Update(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        using var stored = _database.Scans.GetDocument(scan.Id.Value) ?? throw new ScanNotFoundException(scan.Id);
        using var document = stored.ToMutable();
        ScanDocumentMapper.Write(scan, document);
        // Fails only if another thread saved this document between the read above and this save.
        if (!_database.Scans.Save(document, ConcurrencyControl.FailOnConflict))
        {
            throw new InvalidOperationException($"Scan {scan.Id} was changed by someone else; load it again and retry.");
        }

        _logger.LogDebug("Updated scan {ScanId}; sync status {SyncStatus}", scan.Id, scan.SyncStatus);
    }

    public bool Delete(ScanId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        using var document = _database.Scans.GetDocument(id.Value);
        if (document is null) return false;

        // Delete keeps a small tombstone (unlike Purge), so a future replication could tell the server the scan is gone.
        _database.Scans.Delete(document);
        _logger.LogDebug("Deleted scan {ScanId}", id);
        return true;
    }

    public IReadOnlyList<ShipmentScan> ListAll()
    {
        using var query = _database.Database.CreateQuery(ScanQueries.ListAll);
        return ScanQueries.ReadScans(query);
    }

    public IReadOnlyList<ShipmentScan> FindByTrackingNumber(string trackingNumber)
    {
        // Stored tracking numbers are normalized, so the search value must be too; a malformed one is rejected here.
        var normalized = ShipmentScan.NormalizeTrackingNumber(trackingNumber);
        using var query = _database.Database.CreateQuery(ScanQueries.ByTrackingNumber);
        query.Parameters = new Parameters().SetString(ScanQueries.TrackingNumberParameter, normalized);
        return ScanQueries.ReadScans(query);
    }

    public IReadOnlyList<ShipmentScan> ListPending()
    {
        using var query = _database.Database.CreateQuery(ScanQueries.BySyncStatus);
        query.Parameters = new Parameters().SetString(ScanQueries.SyncStatusParameter, nameof(SyncStatus.Pending));
        return ScanQueries.ReadScans(query);
    }

    public IReadOnlyDictionary<ScanType, int> CountByScanType(string courierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courierId);
        using var query = _database.Database.CreateQuery(ScanQueries.CountByScanTypeForCourier);
        query.Parameters = new Parameters().SetString(ScanQueries.CourierIdParameter, courierId.Trim());

        var counts = Enum.GetValues<ScanType>().ToDictionary(scanType => scanType, _ => 0);
        using var results = query.Execute();
        foreach (var row in results.AllResults())
        {
            // A document with an unknown scan type cannot be mapped to a ShipmentScan either; it is skipped here
            // rather than failing the whole count.
            var name = row.GetString("scanType");
            if (name is not null && Enum.GetNames<ScanType>().Contains(name, StringComparer.Ordinal))
            {
                counts[Enum.Parse<ScanType>(name)] = row.GetInt("scanCount");
            }
        }

        return counts;
    }
}
