using System.Threading.Channels;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;

namespace ApexPost.Courier.Offline.Tests.Storage;

public sealed class ScanChangeMonitorTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);
    private readonly TemporaryCourierDatabase _temporary = new();
    private readonly CouchbaseScanRepository _repository;
    private readonly ListLogger<ScanChangeMonitor> _log = new();
    private readonly Channel<ScanChanges> _events = Channel.CreateUnbounded<ScanChanges>();

    public ScanChangeMonitorTests()
    {
        _repository = new CouchbaseScanRepository(_temporary.Database);
    }

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public void The_count_starts_from_the_scans_already_pending()
    {
        Save("APX-10000001");
        var synced = Save("APX-10000002");
        synced.MarkSynced();
        _repository.Update(synced);

        using var monitor = Monitor();

        Assert.Equal(1, monitor.PendingCount);
    }

    [Fact]
    public async Task A_new_scan_is_logged_by_id_and_raises_the_pending_count()
    {
        using var monitor = Monitor();

        var scan = Save("APX-10000001");

        var change = await Next();
        Assert.Equal([scan.Id.Value], change.DocumentIds);
        Assert.Equal(1, change.PendingCount);
        Assert.Equal(1, monitor.PendingCount);
        Assert.Contains(_log.Messages, message => message.Contains(scan.Id.Value, StringComparison.Ordinal) && message.Contains("pending scans: 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Marking_and_deleting_scans_lower_the_count()
    {
        var synced = Save("APX-10000001");
        var deleted = Save("APX-10000002");
        using var monitor = Monitor();

        synced.MarkSynced();
        _repository.Update(synced);
        var afterSync = await Next();
        _repository.Delete(deleted.Id);
        var afterDelete = await Next();

        Assert.Equal((IReadOnlyList<string>)[synced.Id.Value], afterSync.DocumentIds);
        Assert.Equal(1, afterSync.PendingCount);
        Assert.Equal((IReadOnlyList<string>)[deleted.Id.Value], afterDelete.DocumentIds);
        Assert.Equal(0, afterDelete.PendingCount);
    }

    [Fact]
    public async Task An_outbox_run_brings_the_live_count_down_to_zero()
    {
        var scans = Enumerable.Range(1, 3).Select(i => Save($"APX-1000000{i}")).ToList();
        using var monitor = Monitor();

        await new OutboxService(_repository, new FakeShipmentUploader()).SyncPendingAsync();

        var seen = new HashSet<string>();
        while (seen.Count < scans.Count) seen.UnionWith((await Next()).DocumentIds);
        Assert.Equal(scans.Select(scan => scan.Id.Value).ToHashSet(), seen);
        Assert.Equal(0, monitor.PendingCount);
    }

    [Fact]
    public async Task A_disposed_monitor_hears_nothing_more()
    {
        var monitor = Monitor();
        Assert.Equal(1, _temporary.Database.ActiveListenerCount);

        monitor.Dispose();
        monitor.Dispose();
        Save("APX-10000001");

        Assert.Equal(0, _temporary.Database.ActiveListenerCount);
        await AssertNoEvent();
        Assert.Equal(0, monitor.PendingCount);
    }

    [Fact]
    public void Disposing_the_database_removes_the_listener_token()
    {
        var monitor = Monitor();
        using var second = new ScanChangeMonitor(_temporary.Database);
        Assert.Equal(2, _temporary.Database.ActiveListenerCount);

        _temporary.Database.Dispose();

        Assert.Equal(0, _temporary.Database.ActiveListenerCount);
        monitor.Dispose();
    }

    [Fact]
    public void Closing_the_database_waits_for_a_notification_in_progress()
    {
        using var entered = new ManualResetEventSlim();
        var finished = false;
        using var listener = _temporary.Database.AddScanChangeListener((_, _) =>
        {
            entered.Set();
            Thread.Sleep(300);
            Volatile.Write(ref finished, true);
        });

        Save("APX-10000001");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        _temporary.Database.Dispose();

        Assert.True(Volatile.Read(ref finished));
    }

    [Fact]
    public async Task A_failing_subscriber_does_not_stop_later_notifications()
    {
        using var monitor = Monitor();
        var calls = 0;
        monitor.Changed += (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("Subscriber bug");
        };

        Save("APX-10000001");
        await Next();
        Save("APX-10000002");
        var second = await Next();

        Assert.Equal(2, second.PendingCount);
        Assert.Contains(_log.Messages, message => message.Contains("failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_database_is_required()
    {
        Assert.Throws<ArgumentNullException>(() => new ScanChangeMonitor(null!));
    }

    private ScanChangeMonitor Monitor()
    {
        var monitor = new ScanChangeMonitor(_temporary.Database, _log);
        monitor.Changed += (_, change) => _events.Writer.TryWrite(change);
        return monitor;
    }

    private async Task<ScanChanges> Next()
    {
        using var timeout = new CancellationTokenSource(Wait);
        return await _events.Reader.ReadAsync(timeout.Token);
    }

    private async Task AssertNoEvent()
    {
        using var quiet = new CancellationTokenSource(Quiet);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await _events.Reader.ReadAsync(quiet.Token));
    }

    private ShipmentScan Save(string trackingNumber)
    {
        var scan = ShipmentScan.Record(trackingNumber, ScanType.Pickup, "CR-001", "Riverside Depot", DateTimeOffset.UtcNow.AddMinutes(-5));
        _repository.Create(scan);
        return scan;
    }
}
