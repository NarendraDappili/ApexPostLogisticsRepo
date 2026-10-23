namespace ApexPost.Domain.ValueObjects;

/// <summary>Identifies the context in which a money order and its verification are created.</summary>
public sealed record MoneyOrderContextId
{
    public MoneyOrderContextId(string value) => Value = CodeValue.Normalize(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;
}
