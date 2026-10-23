using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Tests.ValueObjects;

public class ParcelMeasurementTests
{
    [Fact]
    public void Parcel_weight_preserves_kilogram_precision()
    {
        var weight = new ParcelWeight(1.234m);

        Assert.Equal(1.234m, weight.Kilograms);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.001)]
    public void Parcel_weight_rejects_non_positive_values(decimal kilograms)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelWeight(kilograms));
    }

    [Fact]
    public void Equivalent_weights_compare_equal()
    {
        Assert.Equal(new ParcelWeight(2.5m), new ParcelWeight(2.5m));
        Assert.NotEqual(new ParcelWeight(2.5m), new ParcelWeight(2.6m));
    }

    [Fact]
    public void Parcel_dimensions_preserve_centimetre_measurements()
    {
        var dimensions = new ParcelDimensions(10.25m, 20.5m, 30.75m);

        Assert.Equal(10.25m, dimensions.LengthCentimetres);
        Assert.Equal(20.5m, dimensions.WidthCentimetres);
        Assert.Equal(30.75m, dimensions.HeightCentimetres);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(-1, 1, 1)]
    public void Parcel_dimensions_reject_non_positive_measurements(
        decimal lengthCentimetres,
        decimal widthCentimetres,
        decimal heightCentimetres)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ParcelDimensions(lengthCentimetres, widthCentimetres, heightCentimetres));
    }

    [Fact]
    public void Equivalent_dimensions_compare_equal()
    {
        Assert.Equal(
            new ParcelDimensions(1m, 2m, 3m),
            new ParcelDimensions(1m, 2m, 3m));
        Assert.NotEqual(
            new ParcelDimensions(1m, 2m, 3m),
            new ParcelDimensions(1m, 2m, 4m));
    }
}
