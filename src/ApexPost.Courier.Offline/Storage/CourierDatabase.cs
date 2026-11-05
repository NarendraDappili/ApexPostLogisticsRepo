using Couchbase.Lite;
using Couchbase.Lite.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.Courier.Offline.Storage;

/// <summary>
/// Owns the courier's local Couchbase Lite database for its whole lifetime: opens <c>apexpost_courier</c>, uses its
/// default collection for shipment scans, creates the query indexes and closes everything on dispose. Couchbase
/// types stay inside this assembly; callers only see this class and the repository.
/// </summary>
public sealed class CourierDatabase : IDisposable
{
    public const string DatabaseName = "apexpost_courier";
    public const string TrackingNumberIndexName = "idx_scans_trackingNumber";
    public const string SyncStatusIndexName = "idx_scans_syncStatus";
    public const string CourierScanTypeIndexName = "idx_scans_courierId_scanType";

    private readonly Database _database;
    private readonly Collection _scans;
    private readonly ILogger<CourierDatabase> _logger;
    private readonly List<ListenerRegistration> _listeners = [];
    private readonly FileStream _processLock;
    private bool _disposed;

    public CourierDatabase(CourierDatabaseOptions options, ILogger<CourierDatabase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        _logger = logger ?? NullLogger<CourierDatabase>.Instance;

        System.IO.Directory.CreateDirectory(options.Directory);
        _processLock = AcquireProcessLock(options.Directory);
        try
        {
            _database = new Database(DatabaseName, new DatabaseConfiguration { Directory = options.Directory });
        }
        catch
        {
            _processLock.Dispose();
            throw;
        }

        try
        {
            _scans = _database.GetDefaultCollection();
            // CreateIndex is a no-op when an index with the same name and definition already exists, so reopening
            // the database does not rebuild them; a changed definition replaces the old index.
            // Each index leads with the filtered field. The second key is the sort or grouping field, so the rows
            // come out of the index already in order and SQLite needs no temporary B-tree (Explain(), A4 tests).
            _scans.CreateIndex(TrackingNumberIndexName, new ValueIndexConfiguration(ScanFields.TrackingNumber, ScanFields.ScannedAtUtc));
            _scans.CreateIndex(SyncStatusIndexName, new ValueIndexConfiguration(ScanFields.SyncStatus, ScanFields.ScannedAtUtc));
            _scans.CreateIndex(CourierScanTypeIndexName, new ValueIndexConfiguration(ScanFields.CourierId, ScanFields.ScanType));
        }
        catch
        {
            _database.Dispose();
            _processLock.Dispose();
            throw;
        }

        _logger.LogInformation("Opened courier database {DatabaseName} at {Path}", DatabaseName, _database.Path);
    }

    /// <summary>
    /// Takes an exclusive lock file next to the database. Couchbase Lite is an embedded database for one process:
    /// a second process writing the same files would not see the first one's change notifications or caches, so it
    /// is refused here instead (OFFLINE-DESIGN.md). The lock goes away with the process, even after a crash.
    /// </summary>
    private static FileStream AcquireProcessLock(string directory)
    {
        var lockPath = System.IO.Path.Combine(directory, DatabaseName + ".lock");
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
        }
        catch (IOException exception)
        {
            throw new CourierDatabaseInUseException(directory, exception);
        }
    }

    /// <summary>
    /// Couchbase Lite writes its own startup and warning lines to the console. A console app that shows its own
    /// log calls this once, before opening the database, so only engine errors still appear.
    /// </summary>
    public static void LimitEngineConsoleLogToErrors() =>
        Couchbase.Lite.Logging.LogSinks.Console = new Couchbase.Lite.Logging.ConsoleLogSink(Couchbase.Lite.Logging.LogLevel.Error);

    /// <summary>The full path of the database folder on disk.</summary>
    public string Path => Database.Path ?? string.Empty;

    /// <summary>The default collection, where every shipment scan document is stored.</summary>
    internal Collection Scans
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _scans;
        }
    }

    internal Database Database
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _database;
        }
    }

    /// <summary>Listener registrations that are still active; <see cref="Dispose"/> removes them.</summary>
    internal int ActiveListenerCount
    {
        get
        {
            lock (_listeners) return _listeners.Count;
        }
    }

    /// <summary>
    /// Registers a change listener on the scans collection. The database keeps the listener token, so disposing the
    /// database removes it even if the caller never disposes the returned registration.
    /// </summary>
    internal IDisposable AddScanChangeListener(EventHandler<CollectionChangedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var registration = new ListenerRegistration(this, Scans.AddChangeListener(handler));
        lock (_listeners) _listeners.Add(registration);
        return registration;
    }

    public void Dispose()
    {
        if (_disposed) return;

        List<ListenerRegistration> remaining;
        lock (_listeners) remaining = [.. _listeners];
        foreach (var registration in remaining) registration.Dispose();
        if (remaining.Count > 0) _logger.LogDebug("Removed {Count} change listener(s) before closing", remaining.Count);

        _disposed = true;
        _scans.Dispose();
        _database.Dispose();
        _processLock.Dispose();
        _logger.LogInformation("Closed courier database {DatabaseName}", DatabaseName);
    }

    private sealed class ListenerRegistration(CourierDatabase owner, ListenerToken token) : IDisposable
    {
        private int _removed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _removed, 1) == 1) return;
            token.Remove();
            lock (owner._listeners) owner._listeners.Remove(this);
        }
    }
}
