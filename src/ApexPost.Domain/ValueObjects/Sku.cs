namespace ApexPost.Domain.ValueObjects;

/// <summary>
/// The business-facing stock-keeping unit printed on shelf labels. It is unique per product and
/// limited to upper-case letters, digits and hyphens so it can be stored as a short varchar code.
/// </summary>
public sealed record Sku
{
    public const int MaxLength = 32;

    public Sku(string value)
    {
        var normalized = CodeValue.Normalize(value, nameof(value));
        if (normalized.Length > MaxLength || !normalized.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            throw new ArgumentException($"A SKU must be at most {MaxLength} letters, digits or hyphens.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
