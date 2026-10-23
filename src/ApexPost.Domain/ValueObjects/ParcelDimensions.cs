namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Represents strictly positive parcel dimensions in centimetres.
/// </summary>
public sealed record ParcelDimensions
{
    public ParcelDimensions(decimal lengthCentimetres, decimal widthCentimetres, decimal heightCentimetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lengthCentimetres);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthCentimetres);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightCentimetres);

        LengthCentimetres = lengthCentimetres;
        WidthCentimetres = widthCentimetres;
        HeightCentimetres = heightCentimetres;
    }

    public decimal LengthCentimetres { get; }

    public decimal WidthCentimetres { get; }

    public decimal HeightCentimetres { get; }
}
