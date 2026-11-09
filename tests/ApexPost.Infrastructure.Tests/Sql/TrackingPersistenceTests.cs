using ApexPost.Application.Abstractions;
using ApexPost.Application.Tracking;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Configurations;
using ApexPost.Infrastructure.Queries;
using ApexPost.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Tests.Sql;

/// <summary>
/// The tracking tables of the AddShipmentTracking migration (Week 4) on a real SQL Server: mapping, keys, constraints,
/// concurrency, the list and history queries, the data-change audit and the API's least-privilege role.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class TrackingPersistenceTests(SqlServerTestDatabase database)
{
    // The tests share one database, so every shipment gets its own number and each list test its own city.
    private static int _nextNumber = 10_000_000;
    private static readonly DateTimeOffset ScanTime = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_shipment_round_trips_with_its_recipient_and_stamped_audit_columns()
    {
        var number = NextNumber();
        await SaveAsync(db => new EfTrackedShipmentRepository(db).Add(Shipment(number, city: "Portside", weightKg: 69.999m)));

        await using var db = database.CreateContext();
        var stored = (await new EfTrackedShipmentRepository(db).FindAsync(new TrackingNumber(number.ToLowerInvariant())))!;

        Assert.Equal((number, TrackingStatus.Created, 69.999m), (stored.TrackingNumber.Value, stored.Status, stored.WeightKg));
        Assert.Equal(new Recipient("Ama Mensah", "+44 20 7946 0000", "12 Harbour Road", "Portside"), stored.Recipient);
        Assert.Equal(ShadowColumns.SystemActor, db.Entry(stored).Property<string>(ShadowColumns.CreatedBy).CurrentValue);
        Assert.NotEmpty(db.Entry(stored).Property<byte[]>(ShadowColumns.RowVersion).CurrentValue);
    }

    [Fact]
    public async Task A_second_shipment_with_the_same_tracking_number_is_a_duplicate_key()
    {
        var number = NextNumber();
        await SaveAsync(db => new EfTrackedShipmentRepository(db).Add(Shipment(number)));

        await Assert.ThrowsAsync<DuplicateKeyException>(() => SaveAsync(db => new EfTrackedShipmentRepository(db).Add(Shipment(number))));
    }

    [Fact]
    public async Task Two_requests_that_read_the_same_version_cannot_both_save()
    {
        var number = NextNumber();
        await SaveAsync(db => new EfTrackedShipmentRepository(db).Add(Shipment(number)));
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var fromFirst = (await new EfTrackedShipmentRepository(first).FindAsync(new(number)))!;
        var fromSecond = (await new EfTrackedShipmentRepository(second).FindAsync(new(number)))!;

        fromFirst.MoveTo(TrackingStatus.PickedUp);
        await new EfUnitOfWork(first).SaveChangesAsync();
        fromSecond.ReplaceDetails(fromSecond.Recipient, 9m);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfUnitOfWork(second).SaveChangesAsync());
    }

    [Fact]
    public async Task Changing_the_recipient_is_one_audited_update_of_the_shipment_row()
    {
        var number = NextNumber();
        await SaveAsync(db => new EfTrackedShipmentRepository(db).Add(Shipment(number)));

        await using (var db = database.CreateContext(null, null, new DataChangeAuditInterceptor(TimeProvider.System)))
        {
            var shipment = (await new EfTrackedShipmentRepository(db).FindAsync(new(number)))!;
            shipment.ReplaceDetails(new Recipient("Kofi Mensah", "+44 20 7946 0000", "12 Harbour Road", "Portside"), 5m);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var change = await check.DataChanges.SingleAsync(row => row.TableName == "TrackedShipments" && row.RecordKey == $"TrackingNumber={number}");
        Assert.Equal("Update", change.Operation);
        Assert.Contains("RecipientName", change.ChangesJson);
        Assert.Contains("Kofi Mensah", change.ChangesJson);
        Assert.DoesNotContain("RecipientPhone", change.ChangesJson);
    }

    [Fact]
    public async Task A_created_shipment_can_be_removed()
    {
        var number = NextNumber();
        await SaveAsync(db => new EfTrackedShipmentRepository(db).Add(Shipment(number)));

        await using (var db = database.CreateContext())
        {
            var repository = new EfTrackedShipmentRepository(db);
            repository.Remove((await repository.FindAsync(new(number)))!);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        Assert.Null(await new EfTrackedShipmentRepository(check).FindAsync(new(number)));
    }

    [Fact]
    public async Task The_scan_history_is_oldest_first_and_the_view_shows_the_latest_scan()
    {
        var number = NextNumber();
        var pickup = ScanId();
        var transit = ScanId();
        await SaveAsync(db =>
        {
            var shipment = Shipment(number);
            new EfTrackedShipmentRepository(db).Add(shipment);
            // Uploaded in the wrong order: the history still sorts by scan time.
            db.Add(shipment.RecordScan(pickup, TrackingScanType.Pickup, "Depot 4", "CR-001", ScanTime, "Left with reception", ScanTime.AddHours(2)));
            db.Add(shipment.RecordScan(transit, TrackingScanType.InTransit, "Central Sorting", "CR-002", ScanTime.AddHours(1), null, ScanTime.AddHours(2)));
        });

        await using var db = database.CreateContext();
        var queries = new EfTrackingQueries(db);
        var history = (await queries.GetScanHistoryAsync(new(number)))!;
        var view = (await queries.GetAsync(new(number)))!;

        Assert.Equal([pickup.Value, transit.Value], history.Select(scan => scan.ScanId));
        Assert.Equal((TrackingScanType.Pickup, "Depot 4", "CR-001", "Left with reception", ScanTime), (history[0].ScanType, history[0].Location, history[0].CourierId, history[0].Notes, history[0].ScannedAtUtc));
        Assert.Equal(TrackingStatus.InTransit, view.Status);
        Assert.Equal(transit.Value, view.LatestScan!.ScanId);
        Assert.Equal(TimeSpan.Zero, view.LatestScan.ScannedAtUtc.Offset);
    }

    [Fact]
    public async Task The_history_of_a_shipment_without_scans_is_empty_and_of_an_unknown_one_null()
    {
        var number = NextNumber();
        await SaveAsync(db => new EfTrackedShipmentRepository(db).Add(Shipment(number)));

        await using var db = database.CreateContext();
        var queries = new EfTrackingQueries(db);

        Assert.Empty((await queries.GetScanHistoryAsync(new(number)))!);
        Assert.Null(await queries.GetScanHistoryAsync(new(NextNumber())));
        Assert.Null((await queries.GetAsync(new(number)))!.LatestScan);
        Assert.Null(await queries.GetAsync(new(NextNumber())));
    }

    [Fact]
    public async Task A_scan_id_is_stored_once_and_needs_a_known_shipment()
    {
        var number = NextNumber();
        var scanId = ScanId();
        await SaveAsync(db =>
        {
            var shipment = Shipment(number);
            new EfTrackedShipmentRepository(db).Add(shipment);
            db.Add(shipment.RecordScan(scanId, TrackingScanType.Pickup, "Depot 4", "CR-001", ScanTime, null, ScanTime));
        });

        // The same device scan ID again, even for another shipment, breaks the unique key that makes uploads idempotent.
        await Assert.ThrowsAsync<DuplicateKeyException>(() => SaveAsync(db =>
        {
            var other = Shipment(NextNumber());
            db.Add(other);
            db.Add(other.RecordScan(scanId, TrackingScanType.Pickup, "Depot 4", "CR-001", ScanTime, null, ScanTime));
        }));
        // A scan for a tracking number that has no shipment row breaks the foreign key.
        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(db => db.Add(Shipment(NextNumber()).RecordScan(ScanId(), TrackingScanType.Pickup, "Depot", "CR-001", ScanTime, null, ScanTime))));
    }

    [Fact]
    public async Task Scan_owners_are_found_in_one_statement_for_known_ids_only()
    {
        var first = NextNumber();
        var second = NextNumber();
        var (a, b, unknown) = (ScanId(), ScanId(), ScanId());
        await SaveAsync(db =>
        {
            var one = Shipment(first);
            var two = Shipment(second);
            var repository = new EfTrackedShipmentRepository(db);
            repository.Add(one);
            repository.Add(two);
            repository.AddScan(one.RecordScan(a, TrackingScanType.Pickup, "Depot 4", "CR-001", ScanTime, null, ScanTime));
            repository.AddScan(two.RecordScan(b, TrackingScanType.Pickup, "Depot 4", "CR-001", ScanTime, null, ScanTime));
        });
        var log = new SqlCommandLog();

        await using var db = database.CreateContext(null, null, log);
        var owners = await new EfTrackedShipmentRepository(db).FindScanOwnersAsync([a, b, unknown, a]);

        Assert.Equal(2, owners.Count);
        Assert.Equal((first, second), (owners[a].Value, owners[b].Value));
        Assert.Single(log.Commands);
        Assert.Empty(await new EfTrackedShipmentRepository(db).FindScanOwnersAsync([]));
        Assert.Single(log.Commands);
    }

    [Fact]
    public async Task The_list_filters_by_status_and_city_and_pages_in_tracking_number_order()
    {
        var city = "City " + Guid.NewGuid().ToString("N")[..8];
        var numbers = Enumerable.Range(0, 5).Select(_ => NextNumber()).ToArray();
        await SaveAsync(db =>
        {
            foreach (var number in numbers.Reverse()) db.Add(Shipment(number, city));
            db.Add(Shipment(NextNumber(), "Elsewhere"));
        });
        await SaveAsync(db => db.Set<TrackedShipment>().Single(shipment => shipment.TrackingNumber == new TrackingNumber(numbers[1])).MoveTo(TrackingStatus.PickedUp));

        await using var db = database.CreateContext();
        var queries = new EfTrackingQueries(db);
        var firstPage = await queries.ListAsync(new TrackingListQuery(1, 2, City: city.ToUpperInvariant()));
        var lastPage = await queries.ListAsync(new TrackingListQuery(3, 2, City: city));
        var pickedUp = await queries.ListAsync(new TrackingListQuery(Status: TrackingStatus.PickedUp, City: city));
        var created = await queries.ListAsync(new TrackingListQuery(Status: TrackingStatus.Created, City: city));

        Assert.Equal((5, 3), (firstPage.TotalCount, firstPage.TotalPages));
        Assert.Equal(numbers[..2], firstPage.Items.Select(item => item.TrackingNumber));
        Assert.Equal([numbers[4]], lastPage.Items.Select(item => item.TrackingNumber));
        Assert.Equal([numbers[1]], pickedUp.Items.Select(item => item.TrackingNumber));
        Assert.Equal(4, created.TotalCount);
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_but_keeps_the_total()
    {
        var city = "City " + Guid.NewGuid().ToString("N")[..8];
        await SaveAsync(db => db.Add(Shipment(NextNumber(), city)));

        await using var db = database.CreateContext();
        var page = await new EfTrackingQueries(db).ListAsync(new TrackingListQuery(5, 20, City: city));

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task A_list_page_is_a_count_the_page_and_the_latest_scans_of_only_that_page()
    {
        var city = "City " + Guid.NewGuid().ToString("N")[..8];
        await SaveAsync(db =>
        {
            for (var i = 0; i < 3; i++)
            {
                var shipment = Shipment(NextNumber(), city);
                db.Add(shipment);
                db.Add(shipment.RecordScan(ScanId(), TrackingScanType.Pickup, "Depot 4", "CR-001", ScanTime, null, ScanTime));
            }
        });
        var log = new SqlCommandLog();

        await using var db = database.CreateContext(null, null, log);
        var page = await new EfTrackingQueries(db).ListAsync(new TrackingListQuery(1, 2, City: city));

        Assert.All(page.Items, item => Assert.NotNull(item.LatestScan));
        Assert.Equal(3, log.Commands.Count);
        Assert.StartsWith("SELECT COUNT(*)", log.Commands[0]);
        // The scans are filtered to the page's tracking numbers before they are numbered: never the whole table.
        var scans = log.Commands[2];
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY", scans);
        Assert.Matches(@"FROM \[TrackingScans\] AS \[\w+\]\s+WHERE \[\w+\]\.\[TrackingNumber\] IN \(@\w+, @\w+\)", scans);
    }

    [Fact]
    public async Task An_empty_list_is_answered_by_the_count_alone()
    {
        var log = new SqlCommandLog();

        await using var db = database.CreateContext(null, null, log);
        var page = await new EfTrackingQueries(db).ListAsync(new TrackingListQuery(City: "No such city " + Guid.NewGuid()));

        Assert.Empty(page.Items);
        Assert.Single(log.Commands);
    }

    [Theory]
    [InlineData("80", "Created", "CK_TrackedShipments_WeightKg")]
    [InlineData("0.05", "Created", "CK_TrackedShipments_WeightKg")]
    [InlineData("5", "Lost", "CK_TrackedShipments_Status")]
    public async Task The_database_refuses_weights_and_statuses_the_domain_refuses(string weight, string status, string constraint)
    {
        await using var db = database.CreateContext();

        var exception = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO dbo.TrackedShipments (TrackingNumber, RecipientName, RecipientPhone, RecipientAddress, City, WeightKg, Status) " +
            "VALUES ({0}, N'Test', '0207946000', N'1 Test Road', N'Test', CAST({1} AS decimal(6,3)), {2})",
            NextNumber(), weight, status));

        Assert.Contains(constraint, exception.Message);
    }

    [Fact]
    public async Task The_api_role_can_do_the_tracking_work_and_nothing_else()
    {
        var user = "api_role_test_" + Guid.NewGuid().ToString("N")[..8];
        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "database", "security", "apexpost-api-role.sql"));
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, script);
        await ExecuteAsync(connection, $"CREATE USER [{user}] WITHOUT LOGIN; ALTER ROLE apexpost_api ADD MEMBER [{user}];");
        var kept = NextNumber();
        var deleted = NextNumber();

        await ExecuteAsync(connection, $"EXECUTE AS USER = '{user}';");
        try
        {
            var options = new DbContextOptionsBuilder<ApexPostDbContext>().UseSqlServer(connection)
                .AddInterceptors(new DataChangeAuditInterceptor(TimeProvider.System), new AuditColumnsInterceptor(TimeProvider.System)).Options;
            await using (var db = new ApexPostDbContext(options))
            {
                var repository = new EfTrackedShipmentRepository(db);
                repository.Add(Shipment(kept));
                repository.Add(Shipment(deleted));
                await new EfUnitOfWork(db).SaveChangesAsync();

                var shipment = (await repository.FindAsync(new(kept)))!;
                db.Add(shipment.RecordScan(ScanId(), TrackingScanType.Pickup, "Depot 4", "CR-001", ScanTime, null, ScanTime));
                repository.Remove((await repository.FindAsync(new(deleted)))!);
                await new EfUnitOfWork(db).SaveChangesAsync();

                Assert.Single((await new EfTrackingQueries(db).GetScanHistoryAsync(new(kept)))!);
                var refused = await Assert.ThrowsAsync<SqlException>(() => db.Branches.AnyAsync());
                Assert.Equal(229, refused.Number);
            }
        }
        finally
        {
            await ExecuteAsync(connection, "REVERT;");
        }

        await using var check = database.CreateContext();
        Assert.Equal(TrackingStatus.PickedUp, (await check.Set<TrackedShipment>().SingleAsync(shipment => shipment.TrackingNumber == new TrackingNumber(kept))).Status);
        Assert.Contains(await check.DataChanges.Where(row => row.TableName == "TrackedShipments").Select(row => row.RecordKey).ToListAsync(), key => key == $"TrackingNumber={deleted}");
    }

    private async Task SaveAsync(Action<ApexPostDbContext> stage)
    {
        await using var db = database.CreateContext();
        stage(db);
        await new EfUnitOfWork(db).SaveChangesAsync();
    }

    private static string NextNumber() => $"APX-{Interlocked.Increment(ref _nextNumber):D8}";

    private static TrackingScanId ScanId() => new("scan::" + Guid.CreateVersion7());

    private static TrackedShipment Shipment(string trackingNumber, string city = "Portside", decimal weightKg = 5m) =>
        new(new TrackingNumber(trackingNumber), new Recipient("Ama Mensah", "+44 20 7946 0000", "12 Harbour Road", city), weightKg);

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ApexPost.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root (ApexPost.slnx) was not found.");
    }
}
