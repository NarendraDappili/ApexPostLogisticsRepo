namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Identifies a shipment using a normalized, non-empty identifier.
/// </summary>
public sealed record ShipmentId
{
    public ShipmentId(string value)
    {
        Value = CodeValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
