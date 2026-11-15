using ApexPost.Application.Abstractions;
using ApexPost.Application.Pricing;
using ApexPost.Application.Repositories;
using ApexPost.Application.Shipping;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.UnitTests.Application.Shipping;

/// <summary>
/// Week 5 (S6), risk R10: the Week 3 counter shipment workflow isolated from its ports with Moq. A refused request
/// must never reach the repository, and a valid one is added exactly once, with the requested values, and saved once.
/// <para>
/// What these mocks do not prove: that the shipment and its price are stored correctly, or that the shipment
/// identifier is unique in the database. CheckoutPersistenceTests and EfRepositoryMappingTests prove that on SQL
/// Server.
/// </para>
/// </summary>
public sealed class ShippingWorkflowServiceInteractionTests
{
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly Mock<IShipmentRepository> _shipments = new();
    private readonly Mock<IShippingRateProvider> _rates = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    public ShippingWorkflowServiceInteractionTests()
    {
        _branches.Setup(branches => branches.FindByCodeAsync(It.Is<BranchCode>(code => code.Value == "BR-A"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Branch(new BranchCode("BR-A"), "Branch A"));
        _rates.Setup(rates => rates.GetRatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ShippingRate(ShipmentType.Parcel, ShippingServiceLevel.Standard, new ZoneCode("ZONE-A"), new Money(8m), new ParcelWeight(5m))]);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_AddsOneShipmentWithTheRequestedValuesAndSavesOnce()
    {
        // Arrange
        _shipments.Setup(shipments => shipments.FindByIdAsync(It.IsAny<ShipmentId>(), It.IsAny<CancellationToken>())).ReturnsAsync((Shipment?)null);

        // Act
        var result = await Service().CreateAsync(Request("SHIP-9", weightKg: 2.5m));

        // Assert
        Assert.Equal(new Money(8m), result.Price);
        _shipments.Verify(shipments => shipments.Add(It.Is<Shipment>(shipment =>
            shipment.Id.Value == "SHIP-9" && shipment.Weight.Kilograms == 2.5m && shipment.OriginBranchCode.Value == "BR-A")), Times.Once);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShipmentIdAlreadyStored_RefusesAndNeverAddsOrSaves()
    {
        // Arrange
        var existing = new Shipment(new ShipmentId("SHIP-1"), new BranchCode("BR-A"), new ZoneCode("ZONE-A"), new ParcelWeight(1m), new ParcelDimensions(10m, 10m, 1m));
        _shipments.Setup(shipments => shipments.FindByIdAsync(It.Is<ShipmentId>(id => id.Value == "SHIP-1"), It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CreateAsync(Request("SHIP-1")));

        // Assert
        Assert.Equal("A shipment with this identifier already exists.", exception.Message);
        _shipments.Verify(shipments => shipments.Add(It.IsAny<Shipment>()), Times.Never);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_UnknownOriginBranch_ThrowsBeforeLookingUpTheShipmentOrTheRates()
    {
        // Act
        await Assert.ThrowsAsync<OriginBranchNotFoundException>(() => Service().CreateAsync(Request("SHIP-2") with { OriginBranchCode = "BR-NOWHERE" }));

        // Assert
        _shipments.Verify(shipments => shipments.FindByIdAsync(It.IsAny<ShipmentId>(), It.IsAny<CancellationToken>()), Times.Never);
        _rates.Verify(rates => rates.GetRatesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _shipments.Verify(shipments => shipments.Add(It.IsAny<Shipment>()), Times.Never);
        _unitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("30.001")]
    public async Task CreateAsync_WeightOutsideTheCounterLimits_IsRefusedBeforeAnyPortIsCalled(string weight)
    {
        // Arrange
        var request = Request("SHIP-3", weightKg: decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture));

        // Act
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service().CreateAsync(request));

        // Assert
        _branches.VerifyNoOtherCalls();
        _shipments.VerifyNoOtherCalls();
        _rates.VerifyNoOtherCalls();
        _unitOfWork.VerifyNoOtherCalls();
    }

    private ShippingWorkflowService Service() => new(_branches.Object, _shipments.Object, _rates.Object, _unitOfWork.Object);

    private static ShipmentCreationRequest Request(string shipmentId, decimal weightKg = 1m) =>
        new(shipmentId, "BR-A", "ZONE-A", ShipmentType.Parcel, ShippingServiceLevel.Standard, weightKg, 10m, 10m, 1m);
}
