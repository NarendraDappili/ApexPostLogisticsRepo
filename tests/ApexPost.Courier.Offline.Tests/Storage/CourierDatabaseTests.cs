using ApexPost.Courier.Offline.Storage;
using Couchbase.Lite;

namespace ApexPost.Courier.Offline.Tests.Storage;

public class CourierDatabaseTests
{
    [Fact]
    public void Opens_the_apexpost_courier_database_in_the_configured_folder()
    {
        using var temporary = new TemporaryCourierDatabase();

        Assert.Equal("apexpost_courier", temporary.Database.Database.Name);
        Assert.StartsWith(temporary.Directory, temporary.Database.Path, StringComparison.OrdinalIgnoreCase);
        Assert.True(Database.Exists(CourierDatabase.DatabaseName, temporary.Directory));
    }

    [Fact]
    public void Stores_scans_in_the_default_collection()
    {
        using var temporary = new TemporaryCourierDatabase();

        Assert.Equal("_default", temporary.Database.Scans.Name);
        Assert.Equal("_default", temporary.Database.Scans.Scope.Name);
    }

    [Fact]
    public void Creates_value_indexes_on_tracking_number_sync_status_and_courier()
    {
        using var temporary = new TemporaryCourierDatabase();

        var indexes = temporary.Database.Scans.GetIndexes();

        Assert.Contains(CourierDatabase.TrackingNumberIndexName, indexes);
        Assert.Contains(CourierDatabase.SyncStatusIndexName, indexes);
        Assert.Contains(CourierDatabase.CourierScanTypeIndexName, indexes);
    }

    [Fact]
    public void Reopening_keeps_documents_and_does_not_duplicate_indexes()
    {
        using var temporary = new TemporaryCourierDatabase();
        using (var document = new MutableDocument("scan::reopen-test"))
        {
            document.SetString(ScanFields.TrackingNumber, "APX-00000001");
            temporary.Database.Scans.Save(document);
        }

        var reopened = temporary.Reopen();

        using var stored = reopened.Scans.GetDocument("scan::reopen-test");
        Assert.NotNull(stored);
        Assert.Equal("APX-00000001", stored.GetString(ScanFields.TrackingNumber));
        Assert.Equal(3, reopened.Scans.GetIndexes().Count);
    }

    [Fact]
    public void Dispose_closes_the_database_and_can_be_called_twice()
    {
        using var temporary = new TemporaryCourierDatabase();
        var database = temporary.Database;

        database.Dispose();
        database.Dispose();

        Assert.Throws<ObjectDisposedException>(() => database.Scans);
        Assert.Throws<ObjectDisposedException>(() => database.Path);
    }

    [Fact]
    public void A_disposed_database_can_be_opened_again_by_the_next_owner()
    {
        using var temporary = new TemporaryCourierDatabase();
        temporary.Database.Dispose();

        using var next = new CourierDatabase(new CourierDatabaseOptions { Directory = temporary.Directory });

        Assert.Equal(0UL, next.Scans.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_missing_folder(string directory)
    {
        Assert.Throws<ArgumentException>(() => new CourierDatabase(new CourierDatabaseOptions { Directory = directory }));
    }

    [Fact]
    public void Rejects_missing_options()
    {
        Assert.Throws<ArgumentNullException>(() => new CourierDatabase(null!));
    }
}
