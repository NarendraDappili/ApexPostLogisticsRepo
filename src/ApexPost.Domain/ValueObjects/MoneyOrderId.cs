namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Identifies a money order using a normalized, non-empty identifier.
/// </summary>
public sealed record MoneyOrderId
{
    public MoneyOrderId(string value)
    {
        Value = CodeValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
