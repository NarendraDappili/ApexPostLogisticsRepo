using System.Reflection;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using Couchbase.Lite.Query;

namespace ApexPost.Courier.Offline.Tests.Storage;

public sealed class ScanQueryTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);
    private readonly TemporaryCourierDatabase _temporary = new();
    private readonly CouchbaseScanRepository _repository;

    public ScanQueryTests()
    {
        _repository = new CouchbaseScanRepository(_temporary.Database);
    }

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public void Find_by_tracking_number_returns_that_parcels_scans_oldest_first()
    {
        var delivered = Save("APX-10000001", ScanType.Delivered, "CR-001", Morning.AddHours(6));
        var pickup = Save("APX-10000001", ScanType.Pickup, "CR-001", Morning);
        Save("APX-10000002", ScanType.Pickup, "CR-001", Morning.AddHours(1));
        var inTransit = Save("APX-10000001", ScanType.InTransit, "CR-002", Morning.AddHours(3));

        var history = _repository.FindByTrackingNumber("APX-10000001");

        Assert.Equal([pickup.Id, inTransit.Id, delivered.Id], history.Select(scan => scan.Id));
        Assert.All(history, scan => Assert.Equal("APX-10000001", scan.TrackingNumber));
    }

    [Fact]
    public void Find_by_tracking_number_accepts_the_number_as_typed()
    {
        var scan = Save("APX-10000001", ScanType.Pickup, "CR-001", Morning);

        Assert.Equal(scan.Id, Assert.Single(_repository.FindByTrackingNumber("  apx-10000001 ")).Id);
    }

    [Fact]
    public void Find_by_tracking_number_is_empty_for_an_unknown_parcel()
    {
        Save("APX-10000001", ScanType.Pickup, "CR-001", Morning);

        Assert.Empty(_repository.FindByTrackingNumber("APX-99999999"));
    }

    [Theory]
    [InlineData("APX-10000001' OR '1'='1")]
    [InlineData("APX-1")]
    [InlineData("")]
    public void Find_by_tracking_number_rejects_a_malformed_number(string trackingNumber)
    {
        Assert.Throws<ArgumentException>(() => _repository.FindByTrackingNumber(trackingNumber));
    }

    [Fact]
    public void List_pending_returns_only_scans_still_waiting_oldest_first()
    {
        var later = Save("APX-10000001", ScanType.InTransit, "CR-001", Morning.AddHours(2));
        var synced = Save("APX-10000002", ScanType.Pickup, "CR-001", Morning.AddHours(1));
        var failed = Save("APX-10000003", ScanType.Pickup, "CR-001", Morning.AddHours(3));
        var earlier = Save("APX-10000004", ScanType.Pickup, "CR-002", Morning);
        synced.MarkSynced();
        _repository.Update(synced);
        failed.MarkFailed();
        _repository.Update(failed);

        var pending = _repository.ListPending();

        Assert.Equal([earlier.Id, later.Id], pending.Select(scan => scan.Id));
        Assert.All(pending, scan => Assert.Equal(SyncStatus.Pending, scan.SyncStatus));
    }

    [Fact]
    public void Count_by_scan_type_counts_one_couriers_scans_and_reports_every_type()
    {
        Save("APX-10000001", ScanType.Pickup, "CR-001", Morning);
        Save("APX-10000002", ScanType.Pickup, "CR-001", Morning);
        Save("APX-10000001", ScanType.Delivered, "CR-001", Morning.AddHours(4));
        Save("APX-10000003", ScanType.Pickup, "CR-002", Morning);
        Save("APX-10000003", ScanType.Failed, "CR-002", Morning.AddHours(2));

        var counts = _repository.CountByScanType("CR-001");

        Assert.Equal(2, counts[ScanType.Pickup]);
        Assert.Equal(0, counts[ScanType.InTransit]);
        Assert.Equal(1, counts[ScanType.Delivered]);
        Assert.Equal(0, counts[ScanType.Failed]);
        Assert.Equal(Enum.GetValues<ScanType>().Length, counts.Count);
        Assert.Equal(2, _repository.CountByScanType(" CR-002 ").Values.Sum());
    }

    [Fact]
    public void Count_by_scan_type_is_all_zero_for_an_unknown_courier()
    {
        Save("APX-10000001", ScanType.Pickup, "CR-001", Morning);

        Assert.All(_repository.CountByScanType("CR-404").Values, count => Assert.Equal(0, count));
    }

    [Theory]
    [InlineData("CR-001' OR '1'='1")]
    [InlineData("CR-001\" OR courierId IS VALUED --")]
    [InlineData("$courierId")]
    public void Input_that_looks_like_query_text_is_only_ever_a_value(string courierId)
    {
        Save("APX-10000001", ScanType.Pickup, "CR-001", Morning);
        Save("APX-10000002", ScanType.Pickup, "CR-002", Morning);

        Assert.All(_repository.CountByScanType(courierId).Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public void Values_with_quotes_are_matched_exactly()
    {
        Save("APX-10000001", ScanType.Delivered, "O'NEIL-01", Morning);

        Assert.Equal(1, _repository.CountByScanType("O'NEIL-01")[ScanType.Delivered]);
    }

    [Fact]
    public void Count_by_scan_type_requires_a_courier()
    {
        Assert.ThrowsAny<ArgumentException>(() => _repository.CountByScanType(" "));
        Assert.ThrowsAny<ArgumentException>(() => _repository.CountByScanType(null!));
    }

    [Fact]
    public void Query_texts_contain_no_literal_values_only_parameters()
    {
        var queries = typeof(ScanQueries)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && !field.Name.EndsWith("Parameter", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(text => text.StartsWith("SELECT", StringComparison.Ordinal))
            .ToList();

        Assert.True(queries.Count >= 4);
        Assert.All(queries, text =>
        {
            Assert.DoesNotContain('\'', text);
            Assert.DoesNotContain('"', text);
            if (text.Contains(" WHERE ", StringComparison.Ordinal)) Assert.Contains(" = $", text, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(ScanQueries.ByTrackingNumber, CourierDatabase.TrackingNumberIndexName)]
    [InlineData(ScanQueries.BySyncStatus, CourierDatabase.SyncStatusIndexName)]
    [InlineData(ScanQueries.CountBySyncStatus, CourierDatabase.SyncStatusIndexName)]
    [InlineData(ScanQueries.CountByScanTypeForCourier, CourierDatabase.CourierScanTypeIndexName)]
    public void Each_filtered_query_searches_its_index_and_needs_no_temporary_sort(string queryText, string indexName)
    {
        var plan = Explain(queryText);

        Assert.Contains($"SEARCH _ USING INDEX {indexName}", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("TEMP B-TREE", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("SCAN _", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void A_database_with_older_single_field_indexes_is_upgraded_on_open()
    {
        var scans = _temporary.Database.Scans;
        scans.DeleteIndex(CourierDatabase.TrackingNumberIndexName);
        scans.CreateIndex(CourierDatabase.TrackingNumberIndexName, new ValueIndexConfiguration("trackingNumber"));
        Assert.Contains("TEMP B-TREE", Explain(ScanQueries.ByTrackingNumber), StringComparison.Ordinal);

        _temporary.Reopen();

        Assert.DoesNotContain("TEMP B-TREE", Explain(ScanQueries.ByTrackingNumber), StringComparison.Ordinal);
    }

    private string Explain(string queryText)
    {
        using var query = _temporary.Database.Database.CreateQuery(queryText);
        return query.Explain();
    }

    private ShipmentScan Save(string trackingNumber, ScanType scanType, string courierId, DateTimeOffset scannedAt)
    {
        var scan = ShipmentScan.Record(trackingNumber, scanType, courierId, "Riverside Depot", scannedAt);
        _repository.Create(scan);
        return scan;
    }
}
