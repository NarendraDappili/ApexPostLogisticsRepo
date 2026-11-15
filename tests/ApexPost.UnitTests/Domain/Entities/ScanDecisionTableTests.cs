using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;

namespace ApexPost.UnitTests.Domain.Entities;

/// <summary>
/// Week 5 (S5), risks R1 and R3: what a scan does to a shipment, for every status and every scan type (24 rows).
/// <see cref="TrackedShipment.EvaluateScan"/> has three ways to accept a scan:
/// <list type="bullet">
/// <item>it moves the shipment to the next status;</item>
/// <item>it repeats the current status (a second hub's InTransit scan);</item>
/// <item>it arrives late from an offline device for a stage already passed (history only).</item>
/// </list>
/// Anything else is refused. A late scan must never move the status back.
/// </summary>
public sealed class ScanDecisionTableTests
{
    [Theory]
    // From Created: only the pickup moves it; a scan that skips stages is refused.
    [InlineData(TrackingStatus.Created, TrackingScanType.Pickup, true, TrackingStatus.PickedUp)]
    [InlineData(TrackingStatus.Created, TrackingScanType.InTransit, false, TrackingStatus.Created)]
    [InlineData(TrackingStatus.Created, TrackingScanType.Delivered, false, TrackingStatus.Created)]
    [InlineData(TrackingStatus.Created, TrackingScanType.Failed, false, TrackingStatus.Created)]
    // From PickedUp: a repeated pickup is history; in transit moves it.
    [InlineData(TrackingStatus.PickedUp, TrackingScanType.Pickup, true, TrackingStatus.PickedUp)]
    [InlineData(TrackingStatus.PickedUp, TrackingScanType.InTransit, true, TrackingStatus.InTransit)]
    [InlineData(TrackingStatus.PickedUp, TrackingScanType.Delivered, false, TrackingStatus.PickedUp)]
    [InlineData(TrackingStatus.PickedUp, TrackingScanType.Failed, false, TrackingStatus.PickedUp)]
    // From InTransit: a late pickup and another hub's scan are history; delivery must wait for OutForDelivery.
    [InlineData(TrackingStatus.InTransit, TrackingScanType.Pickup, true, TrackingStatus.InTransit)]
    [InlineData(TrackingStatus.InTransit, TrackingScanType.InTransit, true, TrackingStatus.InTransit)]
    [InlineData(TrackingStatus.InTransit, TrackingScanType.Delivered, false, TrackingStatus.InTransit)]
    [InlineData(TrackingStatus.InTransit, TrackingScanType.Failed, false, TrackingStatus.InTransit)]
    // From OutForDelivery: late scans are history; delivered or failed ends it.
    [InlineData(TrackingStatus.OutForDelivery, TrackingScanType.Pickup, true, TrackingStatus.OutForDelivery)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingScanType.InTransit, true, TrackingStatus.OutForDelivery)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingScanType.Delivered, true, TrackingStatus.Delivered)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingScanType.Failed, true, TrackingStatus.Failed)]
    // From Delivered (final): late scans are history; a second delivered or a failed is refused.
    [InlineData(TrackingStatus.Delivered, TrackingScanType.Pickup, true, TrackingStatus.Delivered)]
    [InlineData(TrackingStatus.Delivered, TrackingScanType.InTransit, true, TrackingStatus.Delivered)]
    [InlineData(TrackingStatus.Delivered, TrackingScanType.Delivered, false, TrackingStatus.Delivered)]
    [InlineData(TrackingStatus.Delivered, TrackingScanType.Failed, false, TrackingStatus.Delivered)]
    // From Failed (final): the same as Delivered.
    [InlineData(TrackingStatus.Failed, TrackingScanType.Pickup, true, TrackingStatus.Failed)]
    [InlineData(TrackingStatus.Failed, TrackingScanType.InTransit, true, TrackingStatus.Failed)]
    [InlineData(TrackingStatus.Failed, TrackingScanType.Delivered, false, TrackingStatus.Failed)]
    [InlineData(TrackingStatus.Failed, TrackingScanType.Failed, false, TrackingStatus.Failed)]
    public void EvaluateScan_EveryStatusAndScanType_GivesTheDecisionInTheTable(
        TrackingStatus status, TrackingScanType scanType, bool expectedAccepted, TrackingStatus expectedStatusAfter)
    {
        // Arrange
        var shipment = ShipmentAt(status);

        // Act
        var decision = shipment.EvaluateScan(scanType);

        // Assert
        Assert.Equal((expectedAccepted, expectedStatusAfter), (decision.Accepted, decision.StatusAfter));
        Assert.Equal(expectedAccepted, decision.RefusalReason is null);
        Assert.Equal(status, shipment.Status);
    }

    [Fact]
    public void EvaluateScan_EveryRow_TheTableCoversAllStatusesAndScanTypes()
    {
        // Arrange
        var rows = typeof(ScanDecisionTableTests).GetMethod(nameof(EvaluateScan_EveryStatusAndScanType_GivesTheDecisionInTheTable))!
            .GetCustomAttributes(typeof(InlineDataAttribute), inherit: false).Cast<InlineDataAttribute>()
            .Select(row => row.GetData(null!).Single())
            .Select(values => ((TrackingStatus)values[0], (TrackingScanType)values[1]))
            .ToHashSet();

        // Act
        var everyPair = Enum.GetValues<TrackingStatus>().SelectMany(status => Enum.GetValues<TrackingScanType>().Select(type => (status, type))).ToList();

        // Assert: a new status or scan type must get its rows in the table above.
        Assert.Equal(everyPair.Count, rows.Count);
        Assert.All(everyPair, pair => Assert.Contains(pair, rows));
    }

    private static TrackedShipment ShipmentAt(TrackingStatus status)
    {
        var shipment = TrackedShipmentTests.Shipment();
        TrackingStatus[] path = [TrackingStatus.PickedUp, TrackingStatus.InTransit, TrackingStatus.OutForDelivery];
        foreach (var step in path.TakeWhile(_ => shipment.Status != status)) shipment.MoveTo(step);
        if (shipment.Status != status) shipment.MoveTo(status);
        return shipment;
    }
}
