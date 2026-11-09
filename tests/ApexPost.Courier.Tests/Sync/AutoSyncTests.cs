using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApexPost.Courier.Tests.Sync;

/// <summary>
/// D9: Pending scans upload by themselves when the network comes back (the console's ConnectivityChanged), and
/// every interval while online, but never without a network, a token or anything to send. The network here is a
/// switch the test flips; the API is the in-memory FakeApi.
/// </summary>
public sealed class AutoSyncTests : IDisposable
{
    private readonly CourierAppRun _app = new();
    private readonly StringWriter _output = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Scans_saved_offline_upload_when_the_network_comes_back()
    {
        _app.Network.IsAvailable = false;
        using var host = Host();
        SignIn(host);
        var repository = host.Services.GetRequiredService<IScanRepository>();
        repository.Create(Scan());
        repository.Create(Scan());
        var autoSync = host.Services.GetRequiredService<AutoSyncService>();
        await autoSync.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        Assert.Empty(_app.Api.Server.Requests);

        _app.Network.Change(true);

        await Until(() => repository.ListAll().All(scan => scan.SyncStatus == SyncStatus.Synced));
        await Until(() => host.Services.GetRequiredService<ScanChangeMonitor>().PendingCount == 0);
        await autoSync.StopAsync(CancellationToken.None);
        Assert.Equal(2, _app.Api.Stored.Count);
        Assert.Contains("Network is back: uploading Pending scans in 0.01 s", Log, StringComparison.Ordinal);
        Assert.Contains("Auto-sync (network back): 2 synced, 0 failed, 0 still pending", Log, StringComparison.Ordinal);
        Assert.NotNull(host.Services.GetRequiredService<SyncHistory>().LastRun);
    }

    [Fact]
    public async Task Losing_the_network_is_logged_and_nothing_is_sent()
    {
        using var host = Host();
        SignIn(host);
        host.Services.GetRequiredService<IScanRepository>().Create(Scan());
        var autoSync = host.Services.GetRequiredService<AutoSyncService>();
        _app.Network.IsAvailable = false;
        await autoSync.StartAsync(CancellationToken.None);

        _app.Network.Change(false);
        await Task.Delay(200);
        await autoSync.StopAsync(CancellationToken.None);

        Assert.Contains("Network lost: scans are saved on this device until it comes back", Log, StringComparison.Ordinal);
        Assert.Empty(_app.Api.Server.Requests);
    }

    [Fact]
    public async Task While_online_it_tries_every_interval_so_it_finds_the_api_again_after_an_outage()
    {
        // The outage opens the circuit; a short break lets the periodic tries find the API again within the test.
        using var host = Host("--Courier:AutoSync:Interval=00:00:00.200", "--ApexPostApi:Resilience:BreakDuration=00:00:00.500");
        SignIn(host);
        var repository = host.Services.GetRequiredService<IScanRepository>();
        _app.Api.Down = true;
        repository.Create(Scan());
        var autoSync = host.Services.GetRequiredService<AutoSyncService>();
        await autoSync.StartAsync(CancellationToken.None);
        await Until(() => Log.Contains("Auto-sync (start-up) stopped", StringComparison.Ordinal));

        _app.Api.Down = false;

        await Until(() => repository.ListAll().All(scan => scan.SyncStatus == SyncStatus.Synced), seconds: 8);
        await autoSync.StopAsync(CancellationToken.None);
        Assert.Contains("Auto-sync (periodic): 1 synced", Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_token_or_without_pending_scans_nothing_is_sent()
    {
        using var host = Host();
        host.Services.GetRequiredService<CourierSession>().SignIn("CR-001");
        var autoSync = host.Services.GetRequiredService<AutoSyncService>();

        await autoSync.RunOnceAsync("test, nothing pending", CancellationToken.None);
        host.Services.GetRequiredService<IScanRepository>().Create(Scan());
        await Until(() => host.Services.GetRequiredService<ScanChangeMonitor>().PendingCount == 1);
        await autoSync.RunOnceAsync("test, signed in offline", CancellationToken.None);

        Assert.Empty(_app.Api.Server.Requests);
    }

    [Fact]
    public async Task It_can_be_turned_off()
    {
        using var host = Host("--Courier:AutoSync:Enabled=false");
        SignIn(host);
        host.Services.GetRequiredService<IScanRepository>().Create(Scan());
        var autoSync = host.Services.GetRequiredService<AutoSyncService>();

        await autoSync.StartAsync(CancellationToken.None);
        _app.Network.Change(true);
        await Task.Delay(300);
        await autoSync.StopAsync(CancellationToken.None);

        Assert.Empty(_app.Api.Server.Requests);
    }

    private string Log => _output.ToString();

    private IHost Host(params string[] settings) =>
        _app.CreateHost(TextReader.Null, _output, ["--Courier:AutoSync:SettleDelay=00:00:00.010", "--Courier:AutoSync:Interval=00:10:00", .. settings]);

    private static void SignIn(IHost host)
    {
        host.Services.GetRequiredService<CourierSession>().SignIn("CR-001");
        host.Services.GetRequiredService<ITokenStore>().SaveAsync("fake-token-CR-001").AsTask().GetAwaiter().GetResult();
    }

    private static ShipmentScan Scan() => ShipmentScan.Record("APX-30000001", ScanType.Pickup, "CR-001", "Riverside Depot", DateTimeOffset.UtcNow.AddMinutes(-10));

    private static async Task Until(Func<bool> condition, int seconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("The condition was not met in time.");
            await Task.Delay(25);
        }
    }
}
