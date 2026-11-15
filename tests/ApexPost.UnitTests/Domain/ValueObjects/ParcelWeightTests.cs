using System.Globalization;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.UnitTests.Domain.ValueObjects;

/// <summary>
/// Week 5 (S5), risk R7: the counter's parcel weight, more than 0 and at most 30 kg. ParcelMeasurementTests already
/// covers zero and negative weights; the 30 kg ceiling had no test. Weights are passed as text, so each decimal is
/// exact (a double such as 30.001 is not).
/// </summary>
public sealed class ParcelWeightTests
{
    [Theory]
    [InlineData("0.001")]
    [InlineData("1")]
    [InlineData("29.999")]
    [InlineData("30")]
    public void Constructor_WeightUpToTheCounterLimit_KeepsTheKilograms(string kilograms)
    {
        // Arrange
        var weight = decimal.Parse(kilograms, CultureInfo.InvariantCulture);

        // Act
        var parcel = new ParcelWeight(weight);

        // Assert
        Assert.Equal(weight, parcel.Kilograms);
    }

    [Theory]
    [InlineData("30.001")]
    [InlineData("31")]
    [InlineData("1000")]
    public void Constructor_WeightAboveTheCounterLimit_ThrowsNamingTheWeightAndTheLimit(string kilograms)
    {
        // Arrange
        var weight = decimal.Parse(kilograms, CultureInfo.InvariantCulture);

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelWeight(weight));

        // Assert
        Assert.Equal("kilograms", exception.ParamName);
        Assert.Equal(weight, exception.ActualValue);
        Assert.Contains($"'{ParcelWeight.MaximumKilograms}'", exception.Message, StringComparison.Ordinal);
    }
}
