using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;

namespace ApexPost.UnitTests.Domain.Entities;

/// <summary>
/// Week 5 (S5), risk R1: the whole status chain as a table. Created → PickedUp → InTransit → OutForDelivery →
/// Delivered or Failed; every other move is refused. All 36 pairs are listed, so a changed rule fails a named row.
/// </summary>
public sealed class TrackingStatusRulesTests
{
    [Fact]
    public void TrackingStatus_Values_AreTheSixTheTableCovers()
    {
        // Arrange
        TrackingStatus[] covered =
        [
            TrackingStatus.Created, TrackingStatus.PickedUp, TrackingStatus.InTransit,
            TrackingStatus.OutForDelivery, TrackingStatus.Delivered, TrackingStatus.Failed,
        ];

        // Act
        var defined = Enum.GetValues<TrackingStatus>();

        // Assert: a new status must get its rows in IsAllowed_EveryPairOfStatuses_FollowsTheChain.
        Assert.Equal(covered, defined);
    }

    [Theory]
    [InlineData(TrackingStatus.Created, TrackingStatus.Created, false)]
    [InlineData(TrackingStatus.Created, TrackingStatus.PickedUp, true)]
    [InlineData(TrackingStatus.Created, TrackingStatus.InTransit, false)]
    [InlineData(TrackingStatus.Created, TrackingStatus.OutForDelivery, false)]
    [InlineData(TrackingStatus.Created, TrackingStatus.Delivered, false)]
    [InlineData(TrackingStatus.Created, TrackingStatus.Failed, false)]
    [InlineData(TrackingStatus.PickedUp, TrackingStatus.Created, false)]
    [InlineData(TrackingStatus.PickedUp, TrackingStatus.PickedUp, false)]
    [InlineData(TrackingStatus.PickedUp, TrackingStatus.InTransit, true)]
    [InlineData(TrackingStatus.PickedUp, TrackingStatus.OutForDelivery, false)]
    [InlineData(TrackingStatus.PickedUp, TrackingStatus.Delivered, false)]
    [InlineData(TrackingStatus.PickedUp, TrackingStatus.Failed, false)]
    [InlineData(TrackingStatus.InTransit, TrackingStatus.Created, false)]
    [InlineData(TrackingStatus.InTransit, TrackingStatus.PickedUp, false)]
    [InlineData(TrackingStatus.InTransit, TrackingStatus.InTransit, false)]
    [InlineData(TrackingStatus.InTransit, TrackingStatus.OutForDelivery, true)]
    [InlineData(TrackingStatus.InTransit, TrackingStatus.Delivered, false)]
    [InlineData(TrackingStatus.InTransit, TrackingStatus.Failed, false)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingStatus.Created, false)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingStatus.PickedUp, false)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingStatus.InTransit, false)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingStatus.OutForDelivery, false)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingStatus.Delivered, true)]
    [InlineData(TrackingStatus.OutForDelivery, TrackingStatus.Failed, true)]
    [InlineData(TrackingStatus.Delivered, TrackingStatus.Created, false)]
    [InlineData(TrackingStatus.Delivered, TrackingStatus.PickedUp, false)]
    [InlineData(TrackingStatus.Delivered, TrackingStatus.InTransit, false)]
    [InlineData(TrackingStatus.Delivered, TrackingStatus.OutForDelivery, false)]
    [InlineData(TrackingStatus.Delivered, TrackingStatus.Delivered, false)]
    [InlineData(TrackingStatus.Delivered, TrackingStatus.Failed, false)]
    [InlineData(TrackingStatus.Failed, TrackingStatus.Created, false)]
    [InlineData(TrackingStatus.Failed, TrackingStatus.PickedUp, false)]
    [InlineData(TrackingStatus.Failed, TrackingStatus.InTransit, false)]
    [InlineData(TrackingStatus.Failed, TrackingStatus.OutForDelivery, false)]
    [InlineData(TrackingStatus.Failed, TrackingStatus.Delivered, false)]
    [InlineData(TrackingStatus.Failed, TrackingStatus.Failed, false)]
    public void IsAllowed_EveryPairOfStatuses_FollowsTheChain(TrackingStatus from, TrackingStatus to, bool expected)
    {
        // Act
        var allowed = TrackingStatusRules.IsAllowed(from, to);

        // Assert
        Assert.Equal(expected, allowed);
    }

    [Theory]
    [InlineData(TrackingStatus.Created, false)]
    [InlineData(TrackingStatus.PickedUp, false)]
    [InlineData(TrackingStatus.InTransit, false)]
    [InlineData(TrackingStatus.OutForDelivery, false)]
    [InlineData(TrackingStatus.Delivered, true)]
    [InlineData(TrackingStatus.Failed, true)]
    public void IsFinal_EachStatus_OnlyDeliveredAndFailedAreFinal(TrackingStatus status, bool expected)
    {
        // Act
        var final = TrackingStatusRules.IsFinal(status);

        // Assert
        Assert.Equal(expected, final);
    }

    [Fact]
    public void AllowedAfter_UndefinedStatus_ThrowsArgumentOutOfRangeNamingTheStatus()
    {
        // Arrange
        var undefined = (TrackingStatus)99;

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => TrackingStatusRules.AllowedAfter(undefined));

        // Assert
        Assert.Equal("status", exception.ParamName);
    }

    [Fact]
    public void StatusFor_UndefinedScanType_ThrowsArgumentOutOfRangeNamingTheScanType()
    {
        // Arrange
        var undefined = (TrackingScanType)99;

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => TrackingStatusRules.StatusFor(undefined));

        // Assert
        Assert.Equal("scanType", exception.ParamName);
    }

    [Fact]
    public void MoveTo_FinishedShipment_ThrowsSayingItCannotChangeAndKeepsTheStatus()
    {
        // Arrange
        var shipment = TrackedShipmentTests.Shipment();
        shipment.MoveTo(TrackingStatus.PickedUp);
        shipment.MoveTo(TrackingStatus.InTransit);
        shipment.MoveTo(TrackingStatus.OutForDelivery);
        shipment.MoveTo(TrackingStatus.Delivered);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => shipment.MoveTo(TrackingStatus.InTransit));

        // Assert
        Assert.Equal("the shipment is already Delivered and cannot change.", exception.Message);
        Assert.Equal(TrackingStatus.Delivered, shipment.Status);
    }
}
