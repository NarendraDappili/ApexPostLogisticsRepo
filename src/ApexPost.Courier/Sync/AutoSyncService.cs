using System.Threading.Channels;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApexPost.Courier.Sync;

/// <summary>The Courier:AutoSync configuration section.</summary>
internal sealed class AutoSyncOptions
{
    public const string Section = "Courier:AutoSync";

    public bool Enabled { get; set; } = true;

    /// <summary>How long to let a network that just came back settle (DHCP, DNS) before the first upload.</summary>
    public TimeSpan SettleDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often to try anyway while the network is up: the network may stay up while the API was down, and this
    /// is what lets the circuit breaker find the API again without the courier pressing Sync Now.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Uploads Pending scans without the courier asking: when the network comes back (<see cref="INetworkStatus"/>, the
/// console's ConnectivityChanged), at start-up, and every <see cref="AutoSyncOptions.Interval"/> while online. Each
/// run is the outbox's, in batches of 20, and is recorded for Sync Status like a manual sync. It does nothing while
/// there is no network, no token (signed in offline), or nothing Pending. Runs never overlap: the outbox refuses a
/// second one while the first is going.
/// </summary>
internal sealed class AutoSyncService(
    INetworkStatus network,
    ITokenStore tokens,
    ScanChangeMonitor monitor,
    SyncService sync,
    IOptions<AutoSyncOptions> options,
    TimeProvider clock,
    ILogger<AutoSyncService> logger) : BackgroundService
{
    private readonly Channel<string> _triggers = Channel.CreateBounded<string>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>
    /// Listens for network changes before returning: ExecuteAsync runs on its own thread, and a change in the
    /// moment before it got going would otherwise be missed.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Enabled) network.AvailabilityChanged += OnAvailabilityChanged;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        network.AvailabilityChanged -= OnAvailabilityChanged;
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;

        try
        {
            _triggers.Writer.TryWrite("start-up");
            using var timer = new PeriodicTimer(settings.Interval, clock);
            var ticking = TickAsync(timer, stoppingToken);
            await foreach (var reason in _triggers.Reader.ReadAllAsync(stoppingToken))
            {
                await RunOnceAsync(reason, stoppingToken);
            }

            await ticking;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The app is closing.
        }
    }

    /// <summary>One automatic sync, if there is anything to do and a way to do it. Public for tests.</summary>
    internal async Task RunOnceAsync(string reason, CancellationToken cancellationToken)
    {
        if (!network.IsAvailable || monitor.PendingCount == 0 || await tokens.GetAsync(cancellationToken) is null) return;

        var result = await sync.SyncNowAsync(cancellationToken);
        if (result.AlreadyRunning) return;
        if (result.UploadError is null)
        {
            logger.LogInformation("Auto-sync ({Reason}): {Synced} synced, {Failed} failed, {LeftPending} still pending", reason, result.Synced, result.Failed, result.LeftPending);
        }
        else
        {
            logger.LogWarning("Auto-sync ({Reason}) stopped: {Error}", reason, result.UploadError);
        }
    }

    private async Task TickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        while (await timer.WaitForNextTickAsync(stoppingToken)) _triggers.Writer.TryWrite("periodic");
    }

    private void OnAvailabilityChanged(object? sender, bool available)
    {
        if (!available)
        {
            logger.LogInformation("Network lost: scans are saved on this device until it comes back");
            return;
        }

        logger.LogInformation("Network is back: uploading Pending scans in {Delay} s", options.Value.SettleDelay.TotalSeconds);
        _ = Task.Run(async () =>
        {
            await Task.Delay(options.Value.SettleDelay, clock);
            _triggers.Writer.TryWrite("network back");
        });
    }
}
