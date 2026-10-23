namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// Identifies a staff member using a normalized, non-empty identifier.
/// </summary>
public sealed record StaffId
{
    public StaffId(string value)
    {
        Value = CodeValue.Normalize(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}
