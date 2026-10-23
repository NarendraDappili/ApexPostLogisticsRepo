namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Identifies a postal branch using a normalized, non-empty code.
/// </summary>
public sealed record BranchCode
{
    public BranchCode(string value)
    {
        Value = CodeValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
