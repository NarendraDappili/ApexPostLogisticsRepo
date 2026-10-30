namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Represents a parcel's strictly positive weight in kilograms, up to the counter acceptance limit.
/// </summary>
public sealed record ParcelWeight
{
    /// <summary>The heaviest item a counter accepts; individual rates may set lower limits per service.</summary>
    public const int MaximumKilograms = 30;

    public ParcelWeight(decimal kilograms)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kilograms);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(kilograms, MaximumKilograms);

        Kilograms = kilograms;
    }

    public decimal Kilograms { get; }
}
