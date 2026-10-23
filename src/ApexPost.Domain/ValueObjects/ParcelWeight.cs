namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Represents a parcel's strictly positive weight in kilograms.
/// </summary>
public sealed record ParcelWeight
{
    public ParcelWeight(decimal kilograms)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kilograms);

        Kilograms = kilograms;
    }

    public decimal Kilograms { get; }
}
