using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.Entities;

public class ShipmentAndMoneyOrderTests
{
    [Fact]
    public void Shipment_uses_existing_measurement_and_location_value_objects()
    {
        var shipment = new Shipment(
            new ShipmentId("ship-1"),
            new BranchCode("br-001"),
            new ZoneCode("zone-north"),
            new ParcelWeight(2.5m),
            new ParcelDimensions(10m, 20m, 30m));

        Assert.Equal(new ParcelWeight(2.5m), shipment.Weight);
        Assert.Equal(new ParcelDimensions(10m, 20m, 30m), shipment.Dimensions);
    }

    [Fact]
    public void Shipments_with_same_identifier_compare_equal()
    {
        Assert.Equal(
            new Shipment(
                new ShipmentId("ship-1"),
                new BranchCode("BR-001"),
                new ZoneCode("ZONE-1"),
                new ParcelWeight(1m),
                new ParcelDimensions(1m, 1m, 1m)),
            new Shipment(
                new ShipmentId("SHIP-1"),
                new BranchCode("BR-002"),
                new ZoneCode("ZONE-2"),
                new ParcelWeight(2m),
                new ParcelDimensions(2m, 2m, 2m)));
    }

    [Fact]
    public void Money_order_tracks_only_verification_outcome_not_identity_document_data()
    {
        var moneyOrder = new MoneyOrder(
            new MoneyOrderId("mo-1"),
            new BranchCode("BR-001"),
            new Money(250m));

        Assert.Equal(IdentityVerificationOutcome.Pending, moneyOrder.IdentityVerificationOutcome);
        Assert.DoesNotContain(
            typeof(MoneyOrder).GetProperties(),
            property => property.Name.Contains("document", StringComparison.OrdinalIgnoreCase));

        moneyOrder.MarkIdentityVerified();
        Assert.Equal(IdentityVerificationOutcome.Verified, moneyOrder.IdentityVerificationOutcome);
    }

    [Fact]
    public void Money_orders_with_same_identifier_compare_equal()
    {
        Assert.Equal(
            new MoneyOrder(new MoneyOrderId("mo-1"), new BranchCode("BR-001"), new Money(10m)),
            new MoneyOrder(new MoneyOrderId("MO-1"), new BranchCode("BR-002"), new Money(20m)));
    }
}
