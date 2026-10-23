using ApexPost.Application.Pricing;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Pricing;

public class ShippingPriceCalculatorTests
{
    [Theory]
    [InlineData(ShippingServiceLevel.Standard, 8.50)]
    [InlineData(ShippingServiceLevel.Express, 14.75)]
    [InlineData(ShippingServiceLevel.Registered, 11.25)]
    public void Calculate_selects_the_configured_rate_for_each_service_level(
        ShippingServiceLevel serviceLevel,
        decimal expectedCharge)
    {
        var request = CreateRequest(serviceLevel, "ZONE-1", 2m);
        var rates = new[]
        {
            CreateRate(ShippingServiceLevel.Standard, "ZONE-1", 8.50m, 5m),
            CreateRate(ShippingServiceLevel.Express, "ZONE-1", 14.75m, 5m),
            CreateRate(ShippingServiceLevel.Registered, "ZONE-1", 11.25m, 5m)
        };

        var price = ShippingPriceCalculator.Calculate(request, rates);

        Assert.Equal(new Money(expectedCharge), price);
    }

    [Fact]
    public void Calculate_selects_rate_by_destination_zone_and_shipment_type()
    {
        var parcelRequest = CreateRequest(ShippingServiceLevel.Standard, "ZONE-2", 2m, ShipmentType.Parcel);
        var letterRequest = CreateRequest(ShippingServiceLevel.Standard, "ZONE-2", 2m, ShipmentType.Letter);
        var rates = new[]
        {
            CreateRate(ShippingServiceLevel.Standard, "ZONE-1", 8m, 5m, ShipmentType.Parcel),
            CreateRate(ShippingServiceLevel.Standard, "ZONE-2", 9m, 5m, ShipmentType.Parcel),
            CreateRate(ShippingServiceLevel.Standard, "ZONE-2", 4m, 5m, ShipmentType.Letter)
        };

        Assert.Equal(new Money(9m), ShippingPriceCalculator.Calculate(parcelRequest, rates));
        Assert.Equal(new Money(4m), ShippingPriceCalculator.Calculate(letterRequest, rates));
    }

    [Fact]
    public void Calculate_accepts_weight_at_the_configured_limit()
    {
        var request = CreateRequest(ShippingServiceLevel.Standard, "ZONE-1", 5m);
        var rate = CreateRate(ShippingServiceLevel.Standard, "ZONE-1", 8m, 5m);

        Assert.Equal(new Money(8m), ShippingPriceCalculator.Calculate(request, [rate]));
    }

    [Fact]
    public void Calculate_rejects_weight_above_the_configured_limit()
    {
        var request = CreateRequest(ShippingServiceLevel.Standard, "ZONE-1", 5.001m);
        var rate = CreateRate(ShippingServiceLevel.Standard, "ZONE-1", 8m, 5m);

        Assert.Throws<InvalidOperationException>(() => ShippingPriceCalculator.Calculate(request, [rate]));
    }

    [Fact]
    public void Calculate_rejects_missing_or_ambiguous_rates()
    {
        var request = CreateRequest(ShippingServiceLevel.Standard, "ZONE-1", 2m);
        var rate = CreateRate(ShippingServiceLevel.Standard, "ZONE-1", 8m, 5m);

        Assert.Throws<InvalidOperationException>(() => ShippingPriceCalculator.Calculate(request, []));
        Assert.Throws<InvalidOperationException>(() => ShippingPriceCalculator.Calculate(request, [rate, rate]));
    }

    [Fact]
    public void Request_rejects_unknown_service_level()
    {
        var shipment = CreateShipment("ZONE-1", 2m);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ShippingPricingRequest(shipment, ShipmentType.Parcel, (ShippingServiceLevel)99));
    }

    private static ShippingPricingRequest CreateRequest(
        ShippingServiceLevel serviceLevel,
        string zone,
        decimal kilograms,
        ShipmentType shipmentType = ShipmentType.Parcel) =>
        new(CreateShipment(zone, kilograms), shipmentType, serviceLevel);

    private static Shipment CreateShipment(string zone, decimal kilograms) =>
        new(
            new ShipmentId("shipment-1"),
            new BranchCode("BR-001"),
            new ZoneCode(zone),
            new ParcelWeight(kilograms),
            new ParcelDimensions(10m, 20m, 30m));

    private static ShippingRate CreateRate(
        ShippingServiceLevel serviceLevel,
        string zone,
        decimal charge,
        decimal maximumWeight,
        ShipmentType shipmentType = ShipmentType.Parcel) =>
        new(
            shipmentType,
            serviceLevel,
            new ZoneCode(zone),
            new Money(charge),
            new ParcelWeight(maximumWeight));
}
