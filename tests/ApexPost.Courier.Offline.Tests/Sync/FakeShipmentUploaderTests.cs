using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;

namespace ApexPost.Courier.Offline.Tests.Sync;

public class FakeShipmentUploaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeShipmentUploader _uploader = new(new FixedTimeProvider(Now));

    [Fact]
    public async Task A_new_scan_is_accepted_and_the_same_id_again_is_a_duplicate()
    {
        var scan = Scan(Now.AddHours(-1));

        var first = await _uploader.UploadAsync([scan]);
        var second = await _uploader.UploadAsync([scan]);

        Assert.Equal(new ScanUploadOutcome(scan.Id, ScanUploadStatus.Accepted), Assert.Single(first));
        Assert.Equal(new ScanUploadOutcome(scan.Id, ScanUploadStatus.Duplicate), Assert.Single(second));
        Assert.Equal(2, _uploader.Calls.Count);
    }

    [Fact]
    public async Task A_scan_time_in_the_future_is_rejected_beyond_the_allowed_clock_skew()
    {
        var withinSkew = Scan(Now + FakeShipmentUploader.AllowedClockSkew);
        var future = Scan(Now + FakeShipmentUploader.AllowedClockSkew + TimeSpan.FromSeconds(1));

        var outcomes = await _uploader.UploadAsync([withinSkew, future]);

        Assert.Equal(ScanUploadStatus.Accepted, outcomes[0].Status);
        Assert.Equal(ScanUploadStatus.Rejected, outcomes[1].Status);
        Assert.Equal("The scan time is in the future.", outcomes[1].Reason);
    }

    [Fact]
    public async Task A_rejected_scan_is_not_stored_so_a_corrected_upload_is_judged_again()
    {
        var scan = Scan(Now.AddHours(-1));
        _uploader.RejectWhen = candidate => candidate.Location == "Unknown" ? "Location is not a known address." : null;
        var unknown = new ShipmentScan(scan.Id, scan.TrackingNumber, scan.ScanType, scan.CourierId, "Unknown", scan.ScannedAtUtc, null, SyncStatus.Pending);

        var rejected = Assert.Single(await _uploader.UploadAsync([unknown]));
        var retried = Assert.Single(await _uploader.UploadAsync([scan]));

        Assert.Equal(ScanUploadStatus.Rejected, rejected.Status);
        Assert.Equal("Location is not a known address.", rejected.Reason);
        Assert.Equal(ScanUploadStatus.Accepted, retried.Status);
    }

    [Fact]
    public async Task Offline_uploads_fail_like_a_lost_connection()
    {
        _uploader.IsOffline = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => _uploader.UploadAsync([Scan(Now)]));
        Assert.Empty(_uploader.Calls);
    }

    [Fact]
    public async Task A_cancelled_upload_does_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _uploader.UploadAsync([Scan(Now)], cancelled.Token));
        Assert.Empty(_uploader.Calls);
    }

    private static ShipmentScan Scan(DateTimeOffset scannedAt) =>
        ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", "Riverside Depot", scannedAt);
}
