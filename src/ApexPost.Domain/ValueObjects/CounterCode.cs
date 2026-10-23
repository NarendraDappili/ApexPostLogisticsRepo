namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Identifies a service counter using a normalized, non-empty code.
/// </summary>
public sealed record CounterCode
{
    public CounterCode(string value)
    {
        Value = CodeValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
