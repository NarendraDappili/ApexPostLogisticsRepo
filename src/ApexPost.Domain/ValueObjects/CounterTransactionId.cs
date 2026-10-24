namespace ApexPost.Domain.ValueObjects;

public sealed record CounterTransactionId
{
    public CounterTransactionId(string value) => Value = CodeValue.Normalize(value, nameof(value));
    public string Value { get; }
    public override string ToString() => Value;
}
