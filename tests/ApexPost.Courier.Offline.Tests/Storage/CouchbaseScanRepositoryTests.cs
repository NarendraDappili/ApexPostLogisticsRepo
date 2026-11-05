using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;

namespace ApexPost.Courier.Offline.Tests.Storage;

public sealed class CouchbaseScanRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);
    private readonly TemporaryCourierDatabase _temporary = new();
    private CouchbaseScanRepository _repository;

    public CouchbaseScanRepositoryTests()
    {
        _repository = new CouchbaseScanRepository(_temporary.Database);
    }

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public void A_created_scan_can_be_read_back_by_its_id()
    {
        var scan = Scan("APX-10000001", ScanType.Pickup, Morning, "Collected from front desk");

        _repository.Create(scan);
        var loaded = _repository.GetById(scan.Id);

        Assert.NotNull(loaded);
        AssertSameScan(scan, loaded);
        Assert.Equal(SyncStatus.Pending, loaded.SyncStatus);
    }

    [Fact]
    public void The_scan_id_is_the_document_key()
    {
        var scan = Scan("APX-10000001", ScanType.Pickup, Morning);

        _repository.Create(scan);

        using var document = _temporary.Database.Scans.GetDocument(scan.Id.Value);
        Assert.NotNull(document);
        Assert.StartsWith("scan::", document.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_pending_scans_can_be_created()
    {
        var scan = Scan("APX-10000001", ScanType.Pickup, Morning);
        scan.MarkSynced();

        Assert.Throws<ArgumentException>(() => _repository.Create(scan));
        Assert.Null(_repository.GetById(scan.Id));
    }

    [Fact]
    public void Creating_a_second_scan_with_the_same_id_is_refused_without_overwriting()
    {
        var first = Scan("APX-10000001", ScanType.Pickup, Morning);
        var copy = new ShipmentScan(first.Id, "APX-10000002", ScanType.Delivered, "CR-999", "Elsewhere", Morning, null, SyncStatus.Pending);
        _repository.Create(first);

        Assert.Throws<InvalidOperationException>(() => _repository.Create(copy));
        AssertSameScan(first, _repository.GetById(first.Id)!);
    }

    [Fact]
    public void An_unknown_id_reads_as_null()
    {
        Assert.Null(_repository.GetById(ScanId.New()));
    }

    [Fact]
    public void Update_saves_status_and_corrections()
    {
        var synced = Scan("APX-10000001", ScanType.Pickup, Morning);
        var corrected = Scan("APX-10000002", ScanType.Failed, Morning);
        _repository.Create(synced);
        _repository.Create(corrected);

        synced.MarkSynced();
        _repository.Update(synced);
        corrected.MarkFailed();
        _repository.Update(corrected);
        corrected.Correct("Back door, 14 Elm Street", "Gate locked");
        _repository.Update(corrected);

        Assert.Equal(SyncStatus.Synced, _repository.GetById(synced.Id)!.SyncStatus);
        var reloaded = _repository.GetById(corrected.Id)!;
        Assert.Equal(SyncStatus.Pending, reloaded.SyncStatus);
        Assert.Equal("Back door, 14 Elm Street", reloaded.Location);
        Assert.Equal("Gate locked", reloaded.Notes);
    }

    [Fact]
    public void Updating_a_scan_that_is_not_stored_throws_not_found()
    {
        var scan = Scan("APX-10000001", ScanType.Pickup, Morning);

        var exception = Assert.Throws<ScanNotFoundException>(() => _repository.Update(scan));
        Assert.Equal(scan.Id, exception.ScanId);
        Assert.Null(_repository.GetById(scan.Id));
    }

    [Fact]
    public void Delete_removes_the_scan_and_reports_whether_there_was_one()
    {
        var scan = Scan("APX-10000001", ScanType.Pickup, Morning);
        _repository.Create(scan);

        Assert.True(_repository.Delete(scan.Id));
        Assert.False(_repository.Delete(scan.Id));
        Assert.Null(_repository.GetById(scan.Id));
        Assert.Empty(_repository.ListAll());
        Assert.Throws<ScanNotFoundException>(() => _repository.Update(scan));
    }

    [Fact]
    public void List_all_returns_every_scan_oldest_first()
    {
        var latest = Scan("APX-10000001", ScanType.Delivered, Morning.AddHours(5));
        var earliest = Scan("APX-10000002", ScanType.Pickup, Morning);
        var middle = Scan("APX-10000001", ScanType.InTransit, Morning.AddHours(2));
        foreach (var scan in new[] { latest, earliest, middle }) _repository.Create(scan);

        var all = _repository.ListAll();

        Assert.Equal([earliest.Id, middle.Id, latest.Id], all.Select(scan => scan.Id));
        AssertSameScan(latest, all[2]);
    }

    [Fact]
    public void List_all_is_empty_for_a_new_database()
    {
        Assert.Empty(_repository.ListAll());
    }

    [Fact]
    public void Scans_are_still_there_after_the_app_restarts()
    {
        var scan = Scan("APX-10000001", ScanType.Pickup, Morning, "Saved with no network");
        _repository.Create(scan);

        _repository = new CouchbaseScanRepository(_temporary.Reopen());

        AssertSameScan(scan, _repository.GetById(scan.Id)!);
        Assert.Single(_repository.ListAll());
    }

    [Fact]
    public void A_closed_database_cannot_be_used()
    {
        _temporary.Database.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _repository.GetById(ScanId.New()));
        Assert.Throws<ObjectDisposedException>(() => _repository.ListAll());
    }

    [Fact]
    public void Arguments_are_required()
    {
        Assert.Throws<ArgumentNullException>(() => new CouchbaseScanRepository(null!));
        Assert.Throws<ArgumentNullException>(() => _repository.Create(null!));
        Assert.Throws<ArgumentNullException>(() => _repository.GetById(null!));
        Assert.Throws<ArgumentNullException>(() => _repository.Update(null!));
        Assert.Throws<ArgumentNullException>(() => _repository.Delete(null!));
    }

    private static ShipmentScan Scan(string trackingNumber, ScanType scanType, DateTimeOffset scannedAt, string? notes = null) =>
        ShipmentScan.Record(trackingNumber, scanType, "CR-001", "Riverside Depot", scannedAt, notes);

    private static void AssertSameScan(ShipmentScan expected, ShipmentScan actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.TrackingNumber, actual.TrackingNumber);
        Assert.Equal(expected.ScanType, actual.ScanType);
        Assert.Equal(expected.CourierId, actual.CourierId);
        Assert.Equal(expected.Location, actual.Location);
        Assert.Equal(expected.ScannedAtUtc, actual.ScannedAtUtc);
        Assert.Equal(expected.Notes, actual.Notes);
        Assert.Equal(expected.SyncStatus, actual.SyncStatus);
    }
}
