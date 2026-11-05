using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.Courier.Offline.Storage;

/// <summary>The documents in one change notification and the pending count right after it.</summary>
public sealed record ScanChanges(IReadOnlyList<string> DocumentIds, int PendingCount);

/// <summary>
/// Watches the scans collection with a Couchbase Lite change listener. Each notification logs the IDs of the
/// changed documents and recounts the Pending scans, which screens show as a live "pending scans" count. Works with
/// no network: the notifications come from the local database.
/// </summary>
/// <remarks>
/// Couchbase Lite raises notifications on a background thread, one at a time, after the save has committed. A UI
/// must switch to its own thread before touching controls in a <see cref="Changed"/> handler.
/// </remarks>
public sealed class ScanChangeMonitor : IDisposable
{
    private readonly CourierDatabase _database;
    private readonly ILogger<ScanChangeMonitor> _logger;
    private readonly IDisposable _registration;
    private int _pendingCount;

    public ScanChangeMonitor(CourierDatabase database, ILogger<ScanChangeMonitor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _logger = logger ?? NullLogger<ScanChangeMonitor>.Instance;
        _pendingCount = CountPending();
        _registration = database.AddScanChangeListener(OnCollectionChanged);
    }

    /// <summary>Raised after every change notification, with the changed IDs and the new pending count.</summary>
    public event EventHandler<ScanChanges>? Changed;

    /// <summary>The number of scans still waiting for upload, as of the last notification.</summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <summary>Stops listening. Disposing the database also removes the listener.</summary>
    public void Dispose() => _registration.Dispose();

    private void OnCollectionChanged(object? sender, CollectionChangedEventArgs change)
    {
        try
        {
            var count = CountPending();
            Volatile.Write(ref _pendingCount, count);
            var ids = change.DocumentIDs.ToList();
            _logger.LogInformation("Scan documents changed: {DocumentIds}; pending scans: {PendingCount}", string.Join(", ", ids), count);
            Changed?.Invoke(this, new ScanChanges(ids, count));
        }
        catch (ObjectDisposedException)
        {
            // The database closed while this notification was queued; there is nothing left to count.
        }
        catch (Exception exception)
        {
            // A failing subscriber must not break the database's notification thread.
            _logger.LogError(exception, "Handling a scan change notification failed");
        }
    }

    private int CountPending()
    {
        using var query = _database.Database.CreateQuery(ScanQueries.CountBySyncStatus);
        query.Parameters = new Parameters().SetString(ScanQueries.SyncStatusParameter, nameof(SyncStatus.Pending));
        using var results = query.Execute();
        return results.AllResults().FirstOrDefault()?.GetInt("scanCount") ?? 0;
    }
}
