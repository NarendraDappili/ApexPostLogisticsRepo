using System.Diagnostics;
using ApexPost.Courier;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Recording;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApexPost.Specs.Support;

/// <summary>
/// A courier's device, composed the way every app head composes it: <see cref="CourierCoreServiceCollectionExtensions.AddCourierCore"/>
/// gives the typed client, the retry pipeline, the uploader, the outbox, automatic sync and the scan recorder.
/// The head supplies what depends on the device:
/// <list type="bullet">
/// <item>the Couchbase Lite store, in a temporary folder deleted afterwards;</item>
/// <item>a token store;</item>
/// <item>a network status the scenario switches on and off.</item>
/// </list>
/// Its HTTP client is pointed at head office's in-memory test server, so the scans really travel through the API, and
/// no socket is opened.
/// </summary>
public sealed class CourierDevice : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "apexpost-specs-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;

    public CourierDevice(HeadOffice headOffice)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ApexPostApi:BaseAddress"] = headOffice.Server.BaseAddress.ToString() })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new CourierDatabase(new CourierDatabaseOptions { Directory = _folder }, provider.GetRequiredService<ILogger<CourierDatabase>>()));
        services.AddSingleton(provider => new ScanChangeMonitor(provider.GetRequiredService<CourierDatabase>(), provider.GetRequiredService<ILogger<ScanChangeMonitor>>()));
        services.AddSingleton<IScanRepository>(provider => new CouchbaseScanRepository(provider.GetRequiredService<CourierDatabase>(), provider.GetRequiredService<ILogger<CouchbaseScanRepository>>()));
        services.AddCourierCore(configuration);
        services.AddSingleton<ITokenStore>(Tokens);
        services.AddSingleton<INetworkStatus>(Network);
        // The device's requests reach head office's in-memory server instead of a network.
        services.AddHttpClient<ApexApiClient>().ConfigurePrimaryHttpMessageHandler(() => headOffice.Server.CreateHandler());
        _services = services.BuildServiceProvider();
        _ = _services.GetRequiredService<ScanChangeMonitor>();
    }

    public SwitchableNetwork Network { get; } = new();

    public SpecTokenStore Tokens { get; } = new();

    public CourierSession Session => _services.GetRequiredService<CourierSession>();

    public ScanRecorder Recorder => _services.GetRequiredService<ScanRecorder>();

    public IScanRepository Scans => _services.GetRequiredService<IScanRepository>();

    public ApexApiClient Api => _services.GetRequiredService<ApexApiClient>();

    public SyncHistory History => _services.GetRequiredService<SyncHistory>();

    /// <summary>
    /// What the app does when the network comes back or its timer fires: sync, but only with a network, a sign-in and
    /// something Pending. The live Pending count comes from the change listener, a moment after a save, so the device
    /// first lets it catch up, bounded by a Stopwatch rather than the clock.
    /// </summary>
    public async Task SyncAsync()
    {
        var monitor = _services.GetRequiredService<ScanChangeMonitor>();
        var waited = Stopwatch.StartNew();
        while (monitor.PendingCount != Scans.ListPending().Count && waited.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);

        await _services.GetRequiredService<AutoSyncService>().RunOnceAsync("scenario", CancellationToken.None);
    }

    public void Dispose()
    {
        _services.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

/// <summary>The device's network, switched on and off by the scenario.</summary>
public sealed class SwitchableNetwork : INetworkStatus
{
    public bool IsAvailable { get; private set; } = true;

    public event EventHandler<bool>? AvailabilityChanged;

    public void Set(bool available)
    {
        IsAvailable = available;
        AvailabilityChanged?.Invoke(this, available);
    }
}

/// <summary>The device's token store, in memory, as the console app's.</summary>
public sealed class SpecTokenStore : ITokenStore
{
    private string? _token;

    public DateTimeOffset? ExpiresAtUtc => null;

    public ValueTask<string?> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_token);

    public ValueTask SaveAsync(string token, DateTimeOffset? expiresAtUtc = null, CancellationToken cancellationToken = default)
    {
        _token = token;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _token = null;
        return ValueTask.CompletedTask;
    }

    public bool ClearIfCurrent(string token)
    {
        if (_token != token) return false;
        _token = null;
        return true;
    }
}
