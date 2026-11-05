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

    private readonly Database _database;
    private readonly Collection _scans;
    private readonly ILogger<CourierDatabase> _logger;
    private bool _disposed;

    public CourierDatabase(CourierDatabaseOptions options, ILogger<CourierDatabase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        _logger = logger ?? NullLogger<CourierDatabase>.Instance;

        System.IO.Directory.CreateDirectory(options.Directory);
        _database = new Database(DatabaseName, new DatabaseConfiguration { Directory = options.Directory });
        try
        {
            _scans = _database.GetDefaultCollection();
            // CreateIndex is a no-op when an index with the same name and definition already exists, so reopening
            // the database does not rebuild them.
            _scans.CreateIndex(TrackingNumberIndexName, new ValueIndexConfiguration(ScanFields.TrackingNumber));
            _scans.CreateIndex(SyncStatusIndexName, new ValueIndexConfiguration(ScanFields.SyncStatus));
        }
        catch
        {
            _database.Dispose();
            throw;
        }

        _logger.LogInformation("Opened courier database {DatabaseName} at {Path}", DatabaseName, _database.Path);
    }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _scans.Dispose();
        _database.Dispose();
        _logger.LogInformation("Closed courier database {DatabaseName}", DatabaseName);
    }
}
