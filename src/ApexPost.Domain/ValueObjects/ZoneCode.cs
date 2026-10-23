namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Identifies a shipping zone using a normalized, non-empty code.
/// </summary>
public sealed record ZoneCode
{
    public ZoneCode(string value)
    {
        Value = CodeValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
