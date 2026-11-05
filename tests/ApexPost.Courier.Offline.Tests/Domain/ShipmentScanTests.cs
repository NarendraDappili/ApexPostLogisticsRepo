using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Tests.Domain;

public class ShipmentScanTests
{
    private static readonly DateTimeOffset ScannedAt = new(2026, 11, 5, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_recorded_scan_gets_a_new_id_and_waits_to_be_uploaded()
    {
        var scan = ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", "Riverside Depot", ScannedAt, "Left at reception");

        Assert.StartsWith(ScanId.Prefix, scan.Id.Value, StringComparison.Ordinal);
        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
        Assert.Equal("APX-10000001", scan.TrackingNumber);
        Assert.Equal(ScanType.Pickup, scan.ScanType);
        Assert.Equal("CR-001", scan.CourierId);
        Assert.Equal("Riverside Depot", scan.Location);
        Assert.Equal(ScannedAt, scan.ScannedAtUtc);
        Assert.Equal("Left at reception", scan.Notes);
    }

    [Fact]
    public void Text_is_trimmed_and_the_tracking_number_upper_cased()
    {
        var scan = ShipmentScan.Record("  apx-10000001 ", ScanType.InTransit, " CR-001 ", " Hub 3 ", ScannedAt, "  ");

        Assert.Equal("APX-10000001", scan.TrackingNumber);
        Assert.Equal("CR-001", scan.CourierId);
        Assert.Equal("Hub 3", scan.Location);
        Assert.Null(scan.Notes);
    }

    [Fact]
    public void The_scan_time_is_stored_in_utc()
    {
        var local = new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.FromHours(5.5));

        var scan = ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", "Depot", local);

        Assert.Equal(TimeSpan.Zero, scan.ScannedAtUtc.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 9, 30, 0, TimeSpan.Zero), scan.ScannedAtUtc);
    }

    [Theory]
    [InlineData("APX-1234567")]
    [InlineData("APX-123456789")]
    [InlineData("APX-1234567A")]
    [InlineData("APX10000001")]
    [InlineData("ABC-10000001")]
    [InlineData("APX-10000001; DROP")]
    [InlineData("")]
    [InlineData("   ")]
    public void Tracking_numbers_must_be_apx_and_eight_digits(string trackingNumber)
    {
        Assert.Throws<ArgumentException>(() => ShipmentScan.Record(trackingNumber, ScanType.Pickup, "CR-001", "Depot", ScannedAt));
    }

    [Fact]
    public void Required_text_cannot_be_missing()
    {
        Assert.Throws<ArgumentNullException>(() => ShipmentScan.Record(null!, ScanType.Pickup, "CR-001", "Depot", ScannedAt));
        Assert.ThrowsAny<ArgumentException>(() => ShipmentScan.Record("APX-10000001", ScanType.Pickup, " ", "Depot", ScannedAt));
        Assert.ThrowsAny<ArgumentException>(() => ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", "", ScannedAt));
    }

    [Fact]
    public void Text_longer_than_its_limit_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShipmentScan.Record("APX-10000001", ScanType.Pickup, new string('C', ShipmentScan.MaximumCourierIdLength + 1), "Depot", ScannedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", new string('L', ShipmentScan.MaximumLocationLength + 1), ScannedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", "Depot", ScannedAt, new string('N', ShipmentScan.MaximumNotesLength + 1)));
    }

    [Fact]
    public void Undefined_enum_values_and_a_missing_time_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShipmentScan.Record("APX-10000001", (ScanType)99, "CR-001", "Depot", ScannedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ShipmentScan(ScanId.New(), "APX-10000001", ScanType.Pickup, "CR-001", "Depot", ScannedAt, null, (SyncStatus)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", "Depot", default));
        Assert.Throws<ArgumentNullException>(() =>
            new ShipmentScan(null!, "APX-10000001", ScanType.Pickup, "CR-001", "Depot", ScannedAt, null, SyncStatus.Pending));
    }

    [Fact]
    public void The_outbox_can_mark_a_scan_synced_or_failed()
    {
        var synced = ShipmentScan.Record("APX-10000001", ScanType.Delivered, "CR-001", "12 Elm Street", ScannedAt);
        var failed = ShipmentScan.Record("APX-10000002", ScanType.Delivered, "CR-001", "14 Elm Street", ScannedAt);

        synced.MarkSynced();
        failed.MarkFailed();

        Assert.Equal(SyncStatus.Synced, synced.SyncStatus);
        Assert.Equal(SyncStatus.Failed, failed.SyncStatus);
    }

    [Fact]
    public void Correcting_a_failed_scan_queues_it_for_upload_again()
    {
        var scan = ShipmentScan.Record("APX-10000001", ScanType.Failed, "CR-001", "Wrong place", ScannedAt);
        scan.MarkFailed();

        scan.Correct(" 12 Elm Street ", "Nobody home");

        Assert.Equal("12 Elm Street", scan.Location);
        Assert.Equal("Nobody home", scan.Notes);
        Assert.Equal(SyncStatus.Pending, scan.SyncStatus);
    }

    [Fact]
    public void A_synced_scan_cannot_be_corrected()
    {
        var scan = ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", "Depot", ScannedAt);
        scan.MarkSynced();

        Assert.Throws<InvalidOperationException>(() => scan.Correct("Other depot", null));
        Assert.Equal("Depot", scan.Location);
    }
}
