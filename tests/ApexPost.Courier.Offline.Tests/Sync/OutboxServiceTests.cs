using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;

namespace ApexPost.Courier.Offline.Tests.Sync;

public sealed class OutboxServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly TemporaryCourierDatabase _temporary = new();
    private readonly CouchbaseScanRepository _repository;
    private readonly FakeShipmentUploader _server = new(new FixedTimeProvider(Now));
    private readonly ScriptedUploader _uploader;

    public OutboxServiceTests()
    {
        _repository = new CouchbaseScanRepository(_temporary.Database);
        _uploader = new ScriptedUploader(_server);
    }

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public async Task Pending_scans_are_uploaded_and_marked_synced()
    {
        var first = Save("APX-10000001", Now.AddHours(-3));
        var second = Save("APX-10000002", Now.AddHours(-2));

        var result = await Outbox().SyncPendingAsync();

        Assert.Equal(new OutboxSyncResult(2, 2, 0, 0, [], null), result with { Rejections = [] });
        Assert.Empty(result.Rejections);
        Assert.Empty(_repository.ListPending());
        Assert.Equal(SyncStatus.Synced, _repository.GetById(first.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Synced, _repository.GetById(second.Id)!.SyncStatus);
    }

    [Fact]
    public async Task Nothing_pending_means_no_upload_call()
    {
        var result = await Outbox().SyncPendingAsync();

        Assert.Equal(0, result.Attempted);
        Assert.Empty(_server.Calls);
    }

    [Fact]
    public async Task Rejected_scans_are_marked_failed_with_the_servers_reason()
    {
        var good = Save("APX-10000001", Now.AddHours(-1));
        var future = Save("APX-10000002", Now.AddDays(1));

        var result = await Outbox().SyncPendingAsync();

        Assert.Equal(1, result.Synced);
        Assert.Equal(1, result.Failed);
        var rejection = Assert.Single(result.Rejections);
        Assert.Equal(future.Id, rejection.ScanId);
        Assert.Equal("The scan time is in the future.", rejection.Reason);
        Assert.Equal(SyncStatus.Synced, _repository.GetById(good.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Failed, _repository.GetById(future.Id)!.SyncStatus);
    }

    [Fact]
    public async Task With_no_network_every_scan_stays_pending_until_the_next_run()
    {
        Save("APX-10000001", Now.AddHours(-2));
        Save("APX-10000002", Now.AddHours(-1));
        _server.IsOffline = true;
        var outbox = Outbox();

        var offline = await outbox.SyncPendingAsync();

        Assert.Equal(0, offline.Attempted);
        Assert.Equal(2, offline.LeftPending);
        Assert.Contains("no network", offline.UploadError, StringComparison.Ordinal);
        Assert.Equal(2, _repository.ListPending().Count);

        _server.IsOffline = false;
        var online = await outbox.SyncPendingAsync();

        Assert.Equal(2, online.Synced);
        Assert.Null(online.UploadError);
        Assert.Empty(_repository.ListPending());
    }

    [Fact]
    public async Task Scans_go_up_oldest_first_in_batches()
    {
        var saved = Enumerable.Range(0, 45).Select(i => Save($"APX-{10000000 + i}", Now.AddMinutes(-100 + i))).ToList();

        var result = await Outbox().SyncPendingAsync();

        Assert.Equal([20, 20, 5], _server.Calls.Select(call => call.Count));
        Assert.Equal(saved.Select(scan => scan.Id), _server.Calls.SelectMany(call => call));
        Assert.Equal(45, result.Synced);
    }

    [Fact]
    public async Task A_failure_part_way_keeps_the_earlier_batches_synced_and_the_rest_pending()
    {
        var saved = Enumerable.Range(0, 25).Select(i => Save($"APX-{10000000 + i}", Now.AddMinutes(-100 + i))).ToList();
        _uploader.FailOnCall = 2;

        var result = await Outbox().SyncPendingAsync();

        Assert.Equal(20, result.Attempted);
        Assert.Equal(20, result.Synced);
        Assert.Equal(5, result.LeftPending);
        Assert.NotNull(result.UploadError);
        Assert.Equal(saved.Skip(20).Select(scan => scan.Id), _repository.ListPending().Select(scan => scan.Id));
    }

    [Fact]
    public async Task A_scan_the_server_already_has_is_marked_synced_without_a_second_copy()
    {
        // The device uploaded the scan but lost power before it could mark it Synced.
        var scan = Save("APX-10000001", Now.AddHours(-1));
        await _server.UploadAsync([scan]);

        var result = await Outbox().SyncPendingAsync();

        Assert.Equal(1, result.Synced);
        Assert.Equal(SyncStatus.Synced, _repository.GetById(scan.Id)!.SyncStatus);
    }

    [Fact]
    public async Task A_scan_the_server_did_not_answer_for_stays_pending()
    {
        var answered = Save("APX-10000001", Now.AddHours(-2));
        var unanswered = Save("APX-10000002", Now.AddHours(-1));
        _uploader.DropAnswerFor = unanswered.Id;

        var result = await Outbox().SyncPendingAsync();

        Assert.Equal(1, result.Synced);
        Assert.Equal(1, result.LeftPending);
        Assert.Equal(SyncStatus.Synced, _repository.GetById(answered.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Pending, _repository.GetById(unanswered.Id)!.SyncStatus);
    }

    [Fact]
    public async Task A_scan_deleted_during_the_upload_is_not_brought_back()
    {
        var scan = Save("APX-10000001", Now.AddHours(-1));
        _uploader.DuringUpload = () => _repository.Delete(scan.Id);

        var result = await Outbox().SyncPendingAsync();

        Assert.Null(_repository.GetById(scan.Id));
        Assert.Equal(0, result.Synced);
    }

    [Fact]
    public async Task A_correction_made_during_the_upload_is_kept_and_sent_again()
    {
        var scan = Save("APX-10000001", Now.AddHours(-1));
        _server.RejectWhen = candidate => candidate.Location == "Riverside Depot" ? "Unknown depot." : null;
        _uploader.DuringUpload = () =>
        {
            var editing = _repository.GetById(scan.Id)!;
            editing.Correct("Riverside Depot, Gate 2", "Corrected by courier");
            _repository.Update(editing);
        };
        var outbox = Outbox();

        var first = await outbox.SyncPendingAsync();

        var stored = _repository.GetById(scan.Id)!;
        Assert.Equal(0, first.Failed);
        Assert.Equal(SyncStatus.Pending, stored.SyncStatus);
        Assert.Equal("Riverside Depot, Gate 2", stored.Location);

        _uploader.DuringUpload = null;
        var second = await outbox.SyncPendingAsync();

        Assert.Equal(1, second.Synced);
        Assert.Equal(SyncStatus.Synced, _repository.GetById(scan.Id)!.SyncStatus);
    }

    [Fact]
    public async Task A_second_run_started_while_one_is_uploading_does_nothing()
    {
        Save("APX-10000001", Now.AddHours(-1));
        var release = new TaskCompletionSource();
        _uploader.Gate = release.Task;
        var outbox = Outbox();

        var running = outbox.SyncPendingAsync();
        var overlapping = await outbox.SyncPendingAsync();
        release.SetResult();
        var finished = await running;

        Assert.True(overlapping.AlreadyRunning);
        Assert.Equal(0, overlapping.Attempted);
        Assert.Equal(1, finished.Synced);
        Assert.Single(_server.Calls);
    }

    [Fact]
    public async Task Cancelling_stops_the_run_and_leaves_scans_pending()
    {
        Save("APX-10000001", Now.AddHours(-1));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Outbox().SyncPendingAsync(cancelled.Token));
        Assert.Single(_repository.ListPending());
    }

    [Fact]
    public void Arguments_are_checked()
    {
        Assert.Throws<ArgumentNullException>(() => new OutboxService(null!, _server));
        Assert.Throws<ArgumentNullException>(() => new OutboxService(_repository, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboxService(_repository, _server, new OutboxOptions { BatchSize = 0 }));
    }

    private OutboxService Outbox() => new(_repository, _uploader);

    private ShipmentScan Save(string trackingNumber, DateTimeOffset scannedAt)
    {
        var scan = ShipmentScan.Record(trackingNumber, ScanType.Pickup, "CR-001", "Riverside Depot", scannedAt);
        _repository.Create(scan);
        return scan;
    }

    /// <summary>Passes calls to the fake server, with hooks to fail, stall, or change data mid-upload.</summary>
    private sealed class ScriptedUploader(FakeShipmentUploader server) : IShipmentUploader
    {
        private int _calls;

        public int? FailOnCall { get; set; }

        public ScanId? DropAnswerFor { get; set; }

        public Action? DuringUpload { get; set; }

        public Task? Gate { get; set; }

        public async Task<IReadOnlyList<ScanUploadOutcome>> UploadAsync(IReadOnlyList<ShipmentScan> scans, CancellationToken cancellationToken = default)
        {
            if (++_calls == FailOnCall) throw new HttpRequestException("Connection reset (simulated).");
            if (Gate is not null) await Gate;
            DuringUpload?.Invoke();
            var outcomes = await server.UploadAsync(scans, cancellationToken);
            return outcomes.Where(outcome => outcome.ScanId != DropAnswerFor).ToList();
        }
    }
}
