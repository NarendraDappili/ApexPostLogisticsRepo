namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Identifies a catalogue product using a normalized, non-empty identifier.
/// </summary>
public sealed record ProductId
{
    public ProductId(string value)
    {
        Value = CodeValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
