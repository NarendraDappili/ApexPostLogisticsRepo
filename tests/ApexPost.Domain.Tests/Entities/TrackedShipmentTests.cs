using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.Entities;

public sealed class TrackedShipmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_shipment_starts_in_created_and_can_be_deleted()
    {
        var shipment = Shipment();

        Assert.Equal(TrackingStatus.Created, shipment.Status);
        Assert.True(shipment.CanBeDeleted);
        Assert.False(shipment.IsFinished);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(70)]
    [InlineData(31.5)]
    public void Accepts_weights_from_0_1_to_70_kg(double weight)
    {
        Assert.Equal((decimal)weight, Shipment(weightKg: (decimal)weight).WeightKg);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.099)]
    [InlineData(70.001)]
    [InlineData(-1)]
    public void Refuses_weights_outside_0_1_to_70_kg(double weight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Shipment(weightKg: (decimal)weight));
    }

    [Fact]
    public void The_status_follows_the_chain_to_delivered()
    {
        var shipment = Shipment();

        foreach (var status in new[] { TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery, TrackingStatus.Delivered })
        {
            shipment.MoveTo(status);
            Assert.Equal(status, shipment.Status);
        }

        Assert.True(shipment.IsFinished);
        Assert.False(shipment.CanBeDeleted);
    }

    [Fact]
    public void Out_for_delivery_can_end_in_failed()
    {
        var shipment = ShipmentAt(TrackingStatus.OutForDelivery);

        shipment.MoveTo(TrackingStatus.Failed);

        Assert.Equal(TrackingStatus.Failed, shipment.Status);
    }

    [Theory]
    [InlineData(TrackingStatus.Created, TrackingStatus.InTransit)]
    [InlineData(TrackingStatus.Created, TrackingStatus.Delivered)]
    [InlineData(TrackingStatus.Created, TrackingStatus.Created)]
    [InlineData(TrackingStatus.PickedUp, TrackingStatus.Created)]
    [InlineData(TrackingStatus.InTransit, TrackingStatus.Delivered)]
    [InlineData(TrackingStatus.InTransit, TrackingStatus.Failed)]
    [InlineData(TrackingStatus.Delivered, TrackingStatus.Failed)]
    [InlineData(TrackingStatus.Failed, TrackingStatus.OutForDelivery)]
    public void Refuses_any_other_move_and_names_the_allowed_one(TrackingStatus from, TrackingStatus to)
    {
        var shipment = ShipmentAt(from);

        var exception = Assert.Throws<InvalidOperationException>(() => shipment.MoveTo(to));

        Assert.Equal(from, shipment.Status);
        Assert.Contains(from.ToString(), exception.Message);
    }

    [Fact]
    public void Lists_the_allowed_next_statuses()
    {
        Assert.Equal([TrackingStatus.PickedUp], TrackingStatusRules.AllowedAfter(TrackingStatus.Created));
        Assert.Equal([TrackingStatus.Delivered, TrackingStatus.Failed], TrackingStatusRules.AllowedAfter(TrackingStatus.OutForDelivery));
        Assert.Empty(TrackingStatusRules.AllowedAfter(TrackingStatus.Delivered));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrackingStatusRules.AllowedAfter((TrackingStatus)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Shipment().MoveTo((TrackingStatus)99));
    }

    [Fact]
    public void Details_can_be_replaced_until_the_shipment_is_finished()
    {
        var shipment = ShipmentAt(TrackingStatus.OutForDelivery);
        var newRecipient = new Recipient("Kofi Mensah", "0207946001", "3 Quay Street", "Rivermouth");

        shipment.ReplaceDetails(newRecipient, 12.5m);

        Assert.Same(newRecipient, shipment.Recipient);
        Assert.Equal(12.5m, shipment.WeightKg);
        shipment.MoveTo(TrackingStatus.Delivered);
        Assert.Throws<InvalidOperationException>(() => shipment.ReplaceDetails(newRecipient, 1m));
    }

    [Fact]
    public void An_equal_recipient_keeps_the_stored_instance()
    {
        var shipment = Shipment();
        var stored = shipment.Recipient;

        shipment.ReplaceDetails(new Recipient(stored.Name, stored.Phone, stored.Address, stored.City), 2m);

        Assert.Same(stored, shipment.Recipient);
    }

    [Fact]
    public void Replacing_details_checks_the_new_weight()
    {
        var shipment = Shipment();

        Assert.Throws<ArgumentOutOfRangeException>(() => shipment.ReplaceDetails(shipment.Recipient, 70.5m));
        Assert.Equal(5m, shipment.WeightKg);
    }

    [Theory]
    [InlineData(TrackingStatus.Created, TrackingScanType.Pickup, TrackingStatus.PickedUp)]
    [InlineData(TrackingStatus.PickedUp, TrackingScanType.InTransit, TrackingStatus.InTransit)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingScanType.Delivered, TrackingStatus.Delivered)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingScanType.Failed, TrackingStatus.Failed)]
    public void A_scan_for_the_next_status_moves_the_shipment(TrackingStatus from, TrackingScanType scanType, TrackingStatus expected)
    {
        var shipment = ShipmentAt(from);

        var scan = Record(shipment, scanType);

        Assert.Equal(expected, shipment.Status);
        Assert.Equal((shipment.TrackingNumber, scanType), (scan.TrackingNumber, scan.ScanType));
    }

    [Theory]
    [InlineData(TrackingStatus.InTransit, TrackingScanType.InTransit)]
    [InlineData(TrackingStatus.PickedUp, TrackingScanType.Pickup)]
    [InlineData(TrackingStatus.InTransit, TrackingScanType.Pickup)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingScanType.InTransit)]
    [InlineData(TrackingStatus.Delivered, TrackingScanType.Pickup)]
    public void A_repeated_or_late_scan_is_history_and_never_moves_the_status_back(TrackingStatus from, TrackingScanType scanType)
    {
        var shipment = ShipmentAt(from);

        var decision = shipment.EvaluateScan(scanType);
        Record(shipment, scanType);

        Assert.True(decision.Accepted);
        Assert.Equal(from, decision.StatusAfter);
        Assert.Equal(from, shipment.Status);
    }

    [Theory]
    [InlineData(TrackingStatus.Created, TrackingScanType.InTransit)]
    [InlineData(TrackingStatus.Created, TrackingScanType.Delivered)]
    [InlineData(TrackingStatus.InTransit, TrackingScanType.Delivered)]
    [InlineData(TrackingStatus.Delivered, TrackingScanType.Delivered)]
    [InlineData(TrackingStatus.Delivered, TrackingScanType.Failed)]
    [InlineData(TrackingStatus.Failed, TrackingScanType.Delivered)]
    public void A_scan_that_skips_a_stage_or_changes_a_finished_shipment_is_refused_with_the_reason(TrackingStatus from, TrackingScanType scanType)
    {
        var shipment = ShipmentAt(from);

        var decision = shipment.EvaluateScan(scanType);
        var exception = Assert.Throws<InvalidOperationException>(() => Record(shipment, scanType));

        Assert.False(decision.Accepted);
        Assert.Equal(decision.RefusalReason, exception.Message);
        Assert.StartsWith($"A {scanType} scan cannot be recorded", decision.RefusalReason);
        Assert.Equal(from, shipment.Status);
    }

    [Fact]
    public void A_recorded_scan_keeps_its_values_in_utc()
    {
        var shipment = Shipment();
        var id = new TrackingScanId("scan::" + Guid.CreateVersion7());
        var local = new DateTimeOffset(2026, 11, 9, 11, 30, 0, TimeSpan.FromHours(2));

        var scan = shipment.RecordScan(id, TrackingScanType.Pickup, " Depot 4 ", " CR-001 ", local, "  ", Now);

        Assert.Equal((id, "Depot 4", "CR-001", null as string), (scan.Id, scan.Location, scan.CourierId, scan.Notes));
        Assert.Equal(TimeSpan.Zero, scan.ScannedAtUtc.Offset);
        Assert.Equal(local.UtcDateTime, scan.ScannedAtUtc.UtcDateTime);
        Assert.Equal(Now, scan.ReceivedAtUtc);
    }

    [Fact]
    public void A_scan_with_invalid_values_changes_nothing()
    {
        var shipment = Shipment();
        var id = new TrackingScanId("scan::" + Guid.CreateVersion7());

        Assert.ThrowsAny<ArgumentException>(() => shipment.RecordScan(id, TrackingScanType.Pickup, "", "CR-001", Now, null, Now));
        Assert.Throws<ArgumentException>(() => shipment.RecordScan(id, TrackingScanType.Pickup, "Depot", new string('c', TrackingScan.CourierIdMaxLength + 1), Now, null, Now));
        Assert.Throws<ArgumentException>(() => shipment.RecordScan(id, TrackingScanType.Pickup, "Depot", "CR-001", Now, new string('n', TrackingScan.NotesMaxLength + 1), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => shipment.RecordScan(id, (TrackingScanType)9, "Depot", "CR-001", Now, null, Now));
        Assert.Equal(TrackingStatus.Created, shipment.Status);
    }

    internal static TrackedShipment Shipment(string trackingNumber = "APX-24000001", decimal weightKg = 5m) =>
        new(new TrackingNumber(trackingNumber), new Recipient("Ama Mensah", "+44 20 7946 0000", "12 Harbour Road", "Portside"), weightKg);

    private static TrackedShipment ShipmentAt(TrackingStatus status)
    {
        var shipment = Shipment();
        var path = new[] { TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery };
        foreach (var step in path.TakeWhile(step => shipment.Status != status)) shipment.MoveTo(step);
        if (shipment.Status != status) shipment.MoveTo(status);
        return shipment;
    }

    private static TrackingScan Record(TrackedShipment shipment, TrackingScanType scanType) =>
        shipment.RecordScan(new TrackingScanId("scan::" + Guid.CreateVersion7()), scanType, "Central Sorting", "CR-001", Now.AddMinutes(-5), null, Now);
}
