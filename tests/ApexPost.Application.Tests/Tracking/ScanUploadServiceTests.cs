using ApexPost.Application.Abstractions;
using ApexPost.Application.Tracking;
using ApexPost.Application.Validation;
using ApexPost.Domain.Enums;

namespace ApexPost.Application.Tests.Tracking;

/// <summary>B6: the idempotent scan upload, with the scan ID as the key and the status rules deciding each new scan.</summary>
public sealed class ScanUploadServiceTests
{
    private static readonly DateTimeOffset Received = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Scanned = Received.AddHours(-3);

    private readonly InMemoryTrackingStore _store = new();
    private readonly SpyUnitOfWork _unitOfWork = new();

    public ScanUploadServiceTests() => _store.Seed(TrackedShipmentFactory.Create("APX-24000001"));

    [Fact]
    public async Task New_scans_are_accepted_oldest_first_and_move_the_status_in_one_commit()
    {
        // Sent newest first: applied in scan-time order, the pickup moves Created to PickedUp before the in-transit scan.
        var transit = Scan(TrackingScanType.InTransit, Scanned.AddMinutes(40));
        var pickup = Scan(TrackingScanType.Pickup, Scanned);

        var result = await Service().UploadScansAsync("APX-24000001", [transit, pickup]);

        Assert.Equal((2, 0, TrackingStatus.InTransit), (result.Accepted, result.DuplicatesSkipped, result.ShipmentStatus));
        Assert.Empty(result.Rejected);
        Assert.Equal([pickup.ScanId, transit.ScanId], _store.Scans.Select(scan => scan.Id.Value));
        Assert.All(_store.Scans, scan => Assert.Equal(Received, scan.ReceivedAtUtc));
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task Sending_the_same_scans_again_stores_nothing_and_reports_duplicates()
    {
        UploadedScan[] scans = [Scan(TrackingScanType.Pickup, Scanned), Scan(TrackingScanType.InTransit, Scanned.AddMinutes(40))];
        await Service().UploadScansAsync("APX-24000001", scans);

        var again = await Service().UploadScansAsync("APX-24000001", scans);

        Assert.Equal((0, 2, TrackingStatus.InTransit), (again.Accepted, again.DuplicatesSkipped, again.ShipmentStatus));
        Assert.Empty(again.Rejected);
        Assert.Equal(2, _store.Scans.Count);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task A_retry_with_extra_scans_stores_only_the_new_ones()
    {
        var pickup = Scan(TrackingScanType.Pickup, Scanned);
        await Service().UploadScansAsync("APX-24000001", [pickup]);

        var result = await Service().UploadScansAsync("APX-24000001", [pickup, Scan(TrackingScanType.InTransit, Scanned.AddMinutes(40))]);

        Assert.Equal((1, 1), (result.Accepted, result.DuplicatesSkipped));
        Assert.Equal(2, _store.Scans.Count);
    }

    [Fact]
    public async Task A_scan_the_status_rules_refuse_is_rejected_with_the_reason_and_the_rest_still_count()
    {
        var pickup = Scan(TrackingScanType.Pickup, Scanned);
        var delivered = Scan(TrackingScanType.Delivered, Scanned.AddHours(1));

        var result = await Service().UploadScansAsync("APX-24000001", [pickup, delivered]);

        Assert.Equal((1, 0, TrackingStatus.PickedUp), (result.Accepted, result.DuplicatesSkipped, result.ShipmentStatus));
        var rejection = Assert.Single(result.Rejected);
        Assert.Equal(delivered.ScanId, rejection.ScanId);
        Assert.Equal("A Delivered scan cannot be recorded: a shipment cannot move from PickedUp to Delivered; the next status can only be InTransit.", rejection.Reason);
        Assert.Equal([pickup.ScanId], _store.Scans.Select(scan => scan.Id.Value));
    }

    [Fact]
    public async Task A_late_scan_for_a_passed_stage_is_kept_as_history()
    {
        await Service().ChangeStatusAsync("APX-24000001", TrackingStatus.PickedUp);
        await Service().ChangeStatusAsync("APX-24000001", TrackingStatus.InTransit);

        var result = await Service().UploadScansAsync("APX-24000001", [Scan(TrackingScanType.Pickup, Scanned)]);

        Assert.Equal((1, TrackingStatus.InTransit), (result.Accepted, result.ShipmentStatus));
    }

    [Fact]
    public async Task A_scan_id_stored_for_another_shipment_is_rejected_without_naming_it()
    {
        _store.Seed(TrackedShipmentFactory.Create("APX-24000002"));
        var scan = Scan(TrackingScanType.Pickup, Scanned);
        await Service().UploadScansAsync("APX-24000002", [scan]);

        var result = await Service().UploadScansAsync("APX-24000001", [scan]);

        Assert.Equal((0, 0), (result.Accepted, result.DuplicatesSkipped));
        Assert.Equal(new ScanRejection(scan.ScanId, "This scan ID is already recorded for a different shipment."), Assert.Single(result.Rejected));
        Assert.DoesNotContain("APX-24000002", result.Rejected[0].Reason);
        Assert.Equal(TrackingStatus.Created, result.ShipmentStatus);
    }

    [Fact]
    public async Task The_same_scan_twice_in_one_upload_is_stored_once()
    {
        var scan = Scan(TrackingScanType.Pickup, Scanned);

        var result = await Service().UploadScansAsync("APX-24000001", [scan, scan]);

        Assert.Equal((1, 1), (result.Accepted, result.DuplicatesSkipped));
        Assert.Single(_store.Scans);
    }

    [Fact]
    public async Task Malformed_scans_are_rejected_in_request_order_and_leave_the_status_alone()
    {
        var badId = Scan(TrackingScanType.Pickup, Scanned) with { ScanId = "scan::nope" };
        var badType = Scan((TrackingScanType)9, Scanned);
        var badLocation = Scan(TrackingScanType.Pickup, Scanned) with { Location = new string('x', 101) };

        var result = await Service().UploadScansAsync("APX-24000001", [badId, badType, badLocation]);

        Assert.Equal(
        [
            new ScanRejection("scan::nope", "The scan ID must be scan:: followed by a GUID."),
            new ScanRejection(badType.ScanId, "The scan type is not a known scan type."),
            new ScanRejection(badLocation.ScanId, "location: Must be at most 100 characters."),
        ], result.Rejected);
        Assert.Equal(TrackingStatus.Created, result.ShipmentStatus);
        Assert.Empty(_store.Scans);
    }

    [Fact]
    public async Task Nothing_accepted_means_nothing_committed()
    {
        await Service().UploadScansAsync("APX-24000001", [Scan(TrackingScanType.Delivered, Scanned)]);

        Assert.Equal(0, _unitOfWork.Attempts);
    }

    [Theory]
    [InlineData(typeof(ConcurrencyConflictException))]
    [InlineData(typeof(DuplicateKeyException))]
    public async Task A_concurrent_upload_that_wins_the_race_makes_this_one_a_retryable_conflict(Type conflictType)
    {
        var conflict = (Exception)Activator.CreateInstance(conflictType, "conflict", null)!;
        var service = new ShipmentTrackingService(_store, _store, new SpyUnitOfWork { FailWith = conflict }, new FixedClock(Received));

        var exception = await Assert.ThrowsAsync<ScanUploadConflictException>(() => service.UploadScansAsync("APX-24000001", [Scan(TrackingScanType.Pickup, Scanned)]));

        Assert.IsAssignableFrom<ConflictException>(exception);
        Assert.Same(conflict, exception.InnerException);
        Assert.Contains("Send the upload again", exception.Message);
    }

    [Fact]
    public async Task An_unknown_shipment_is_not_found_and_an_empty_upload_is_invalid()
    {
        await Assert.ThrowsAsync<TrackedShipmentNotFoundException>(() => Service().UploadScansAsync("APX-99999999", [Scan(TrackingScanType.Pickup, Scanned)]));
        await Assert.ThrowsAsync<RequestValidationException>(() => Service().UploadScansAsync("APX-24000001", []));
    }

    private ShipmentTrackingService Service() => new(_store, _store, _unitOfWork, new FixedClock(Received));

    private static UploadedScan Scan(TrackingScanType type, DateTimeOffset scannedAt) =>
        new("scan::" + Guid.CreateVersion7(), type, "Depot 4", "CR-001", scannedAt, null);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
