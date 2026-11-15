using ApexPost.Application.Abstractions;
using ApexPost.Application.Repositories;
using ApexPost.Application.Tracking;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.UnitTests.Domain.Entities;

namespace ApexPost.UnitTests.Application.Tracking;

/// <summary>
/// Week 5 (S6), risks R1 and R2: what <see cref="ShipmentTrackingService"/> asks of its repository and unit of work,
/// isolated with Moq. ShipmentTrackingServiceTests covers the behaviour over a stateful in-memory store; these tests
/// are about the calls: what is looked up, what is added, and what is never saved.
/// <para>
/// What these mocks do not prove: that the unique index on the tracking number exists, or that two creates racing
/// in SQL Server become error 2601/2627 and then a <see cref="DuplicateKeyException"/>. That is proven on a real
/// database by TrackingPersistenceTests and ShipmentEndpointTests (TESTING-STRATEGY.md §7.3).
/// </para>
/// </summary>
public sealed class ShipmentTrackingServiceInteractionTests
{
    private readonly Mock<ITrackedShipmentRepository> _shipments = new();
    private readonly Mock<ITrackingQueries> _queries = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    [Fact]
    public async Task CreateAsync_TrackingNumberAlreadyStored_ThrowsInUseAndNeverAddsOrSaves()
    {
        // Arrange
        _shipments.Setup(repository => repository.FindAsync(It.Is<TrackingNumber>(number => number.Value == "APX-24000001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TrackedShipmentTests.Shipment("APX-24000001"));

        // Act
        var exception = await Assert.ThrowsAsync<TrackingNumberInUseException>(() => Service().CreateAsync(NewShipment("APX-24000001")));

        // Assert
        Assert.Equal("APX-24000001", exception.TrackingNumber);
        _shipments.Verify(repository => repository.Add(It.IsAny<TrackedShipment>()), Times.Never);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NewNumberTypedInLowerCase_LooksUpTheNormalisedNumberThenAddsOnceAndSavesOnce()
    {
        // Arrange
        var calls = new List<string>();
        _shipments.Setup(repository => repository.FindAsync(It.IsAny<TrackingNumber>(), It.IsAny<CancellationToken>())).ReturnsAsync((TrackedShipment?)null);
        _shipments.Setup(repository => repository.Add(It.IsAny<TrackedShipment>())).Callback(() => calls.Add("Add"));
        _unitOfWork.Setup(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => calls.Add("Save")).ReturnsAsync(1);

        // Act
        var created = await Service().CreateAsync(NewShipment(" apx-24000002 ", weightKg: 2.5m));

        // Assert
        Assert.Equal(("APX-24000002", TrackingStatus.Created), (created.TrackingNumber, created.Status));
        _shipments.Verify(repository => repository.FindAsync(It.Is<TrackingNumber>(number => number.Value == "APX-24000002"), It.IsAny<CancellationToken>()), Times.Once);
        _shipments.Verify(repository => repository.Add(It.Is<TrackedShipment>(shipment =>
            shipment.TrackingNumber.Value == "APX-24000002" && shipment.WeightKg == 2.5m && shipment.Status == TrackingStatus.Created)), Times.Once);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["Add", "Save"], calls);
    }

    [Fact]
    public async Task CreateAsync_AnotherCreateWinsTheRaceAtSave_ThrowsInUseCausedByTheDuplicateKey()
    {
        // Arrange: the pre-check finds nothing, then the database's unique key refuses the insert.
        var duplicate = new DuplicateKeyException("A unique business key is already in use.");
        _shipments.Setup(repository => repository.FindAsync(It.IsAny<TrackingNumber>(), It.IsAny<CancellationToken>())).ReturnsAsync((TrackedShipment?)null);
        _unitOfWork.Setup(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(duplicate);

        // Act
        var exception = await Assert.ThrowsAsync<TrackingNumberInUseException>(() => Service().CreateAsync(NewShipment("APX-24000003")));

        // Assert
        Assert.Equal("APX-24000003", exception.TrackingNumber);
        Assert.Same(duplicate, exception.InnerException);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeStatusAsync_MoveTheChainForbids_ThrowsNamingTheAllowedStatusAndNeverSaves()
    {
        // Arrange
        var shipment = TrackedShipmentTests.Shipment("APX-24000004");
        _shipments.Setup(repository => repository.FindAsync(It.IsAny<TrackingNumber>(), It.IsAny<CancellationToken>())).ReturnsAsync(shipment);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidStatusTransitionException>(() => Service().ChangeStatusAsync("APX-24000004", TrackingStatus.Delivered));

        // Assert
        Assert.Equal((TrackingStatus.Created, TrackingStatus.Delivered), (exception.From, exception.To));
        Assert.Equal([TrackingStatus.PickedUp], exception.Allowed);
        Assert.Equal(TrackingStatus.Created, shipment.Status);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeStatusAsync_AllowedMove_SavesOnceThenAnswersFromTheQueries()
    {
        // Arrange
        var shipment = TrackedShipmentTests.Shipment("APX-24000005");
        _shipments.Setup(repository => repository.FindAsync(It.IsAny<TrackingNumber>(), It.IsAny<CancellationToken>())).ReturnsAsync(shipment);
        _queries.Setup(queries => queries.GetAsync(It.IsAny<TrackingNumber>(), It.IsAny<CancellationToken>())).ReturnsAsync((TrackedShipmentView?)null);

        // Act
        var view = await Service().ChangeStatusAsync("APX-24000005", TrackingStatus.PickedUp);

        // Assert
        Assert.Equal(TrackingStatus.PickedUp, view.Status);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _queries.Verify(queries => queries.GetAsync(It.Is<TrackingNumber>(number => number.Value == "APX-24000005"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeStatusAsync_UnknownTrackingNumber_ThrowsNotFoundAndNeverSaves()
    {
        // Arrange
        _shipments.Setup(repository => repository.FindAsync(It.IsAny<TrackingNumber>(), It.IsAny<CancellationToken>())).ReturnsAsync((TrackedShipment?)null);

        // Act
        var exception = await Assert.ThrowsAsync<TrackedShipmentNotFoundException>(() => Service().ChangeStatusAsync("APX-24000006", TrackingStatus.PickedUp));

        // Assert
        Assert.Equal("APX-24000006", exception.TrackingNumber);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ShipmentAlreadyPickedUp_ThrowsNotDeletableAndNeverRemovesOrSaves()
    {
        // Arrange
        var shipment = TrackedShipmentTests.Shipment("APX-24000007");
        shipment.MoveTo(TrackingStatus.PickedUp);
        _shipments.Setup(repository => repository.FindAsync(It.IsAny<TrackingNumber>(), It.IsAny<CancellationToken>())).ReturnsAsync(shipment);

        // Act
        await Assert.ThrowsAsync<ShipmentNotDeletableException>(() => Service().DeleteAsync("APX-24000007"));

        // Assert
        _shipments.Verify(repository => repository.Remove(It.IsAny<TrackedShipment>()), Times.Never);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private ShipmentTrackingService Service() =>
        new(_shipments.Object, _queries.Object, _unitOfWork.Object, new FakeTimeProvider(TestTime.Now));

    private static NewTrackedShipment NewShipment(string trackingNumber, decimal weightKg = 5m) =>
        new(trackingNumber, "Ama Mensah", "+44 20 7946 0000", "12 Harbour Road", "Portside", weightKg);
}
