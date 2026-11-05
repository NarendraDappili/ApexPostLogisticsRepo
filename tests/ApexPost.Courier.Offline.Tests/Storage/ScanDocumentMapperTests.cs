using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using Couchbase.Lite;

namespace ApexPost.Courier.Offline.Tests.Storage;

public class ScanDocumentMapperTests
{
    private static readonly DateTimeOffset ScannedAt = new(2026, 11, 5, 9, 30, 15, 123, TimeSpan.Zero);

    [Fact]
    public void A_scan_survives_a_save_and_load_through_the_database()
    {
        using var temporary = new TemporaryCourierDatabase();
        var scan = ShipmentScan.Record("APX-10000001", ScanType.Delivered, "CR-001", "12 Elm Street", ScannedAt, "Signed by A. Fictional");
        scan.MarkFailed();

        using (var document = ScanDocumentMapper.ToNewDocument(scan)) temporary.Database.Scans.Save(document);
        using var stored = temporary.Database.Scans.GetDocument(scan.Id.Value);
        var loaded = ScanDocumentMapper.ToScan(stored!.Id, stored);

        Assert.Equal(scan.Id, loaded.Id);
        Assert.Equal(scan.TrackingNumber, loaded.TrackingNumber);
        Assert.Equal(scan.ScanType, loaded.ScanType);
        Assert.Equal(scan.CourierId, loaded.CourierId);
        Assert.Equal(scan.Location, loaded.Location);
        Assert.Equal(scan.ScannedAtUtc, loaded.ScannedAtUtc);
        Assert.Equal(scan.Notes, loaded.Notes);
        Assert.Equal(SyncStatus.Failed, loaded.SyncStatus);
    }

    [Fact]
    public void The_document_uses_the_scan_id_and_the_agreed_json_fields()
    {
        var scan = ShipmentScan.Record("APX-10000001", ScanType.InTransit, "CR-001", "Hub 3", ScannedAt);

        using var document = ScanDocumentMapper.ToNewDocument(scan);

        Assert.Equal(scan.Id.Value, document.Id);
        Assert.Equal(
            ["courierId", "location", "notes", "scanType", "scannedAtUtc", "syncStatus", "trackingNumber"],
            document.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("InTransit", document.GetString("scanType"));
        Assert.Equal("Pending", document.GetString("syncStatus"));
        Assert.Equal("2026-11-05T09:30:15.1230000Z", document.GetString("scannedAtUtc"));
        Assert.Null(document.GetString("notes"));
    }

    [Fact]
    public void Stored_times_sort_as_text_in_time_order()
    {
        DateTimeOffset[] times =
        [
            new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero),
            new(2026, 11, 5, 9, 0, 0, 1, TimeSpan.Zero),
            new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero),
            new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ];

        var texts = times.Reverse().Select(ScanDocumentMapper.FormatTimestamp).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(times.Select(ScanDocumentMapper.FormatTimestamp), texts);
    }

    [Theory]
    [InlineData("trackingNumber", null)]
    [InlineData("trackingNumber", "APX-1")]
    [InlineData("scanType", "7")]
    [InlineData("scanType", "pickup")]
    [InlineData("scanType", "Pickup, Delivered")]
    [InlineData("syncStatus", "Done")]
    [InlineData("scannedAtUtc", "yesterday")]
    [InlineData("courierId", null)]
    [InlineData("location", null)]
    public void A_document_that_is_not_a_valid_scan_is_reported_as_invalid_data(string field, string? value)
    {
        var scan = ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", "Depot", ScannedAt);
        using var document = ScanDocumentMapper.ToNewDocument(scan);
        if (value is null) document.Remove(field);
        else document.SetString(field, value);

        var exception = Assert.Throws<InvalidDataException>(() => ScanDocumentMapper.ToScan(document.Id, document));
        Assert.Contains(scan.Id.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_a_scan_id_is_reported_as_invalid_data()
    {
        var scan = ShipmentScan.Record("APX-10000001", ScanType.Pickup, "CR-001", "Depot", ScannedAt);
        using var document = new MutableDocument("parcel::1");
        ScanDocumentMapper.Write(scan, document);

        Assert.Throws<InvalidDataException>(() => ScanDocumentMapper.ToScan(document.Id, document));
    }
}
