namespace ApexPost.Domain.Entities;

/// <summary>Groups catalogue products for browsing and reporting, for example stamps or packaging.</summary>
public sealed class ProductCategory : IEquatable<ProductCategory>
{
    public const string GeneralCode = "GENERAL";
    public const int MaxCodeLength = 20;

    public ProductCategory(string code, string name)
    {
        Code = NormalizeCode(code);
        Name = name;
    }

    public string Code { get; }

    public string Name
    {
        get;
        private set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Name));
            field = value.Trim();
        }
    }

    public void Rename(string name) => Name = name;

    public bool Equals(ProductCategory? other) => other is not null && Code == other.Code;

    public override bool Equals(object? obj) => obj is ProductCategory other && Equals(other);

    public override int GetHashCode() => Code.GetHashCode(StringComparison.Ordinal);

    /// <summary>Category codes are upper-case and short; products refer to their category by this code.</summary>
    public static string NormalizeCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > MaxCodeLength) throw new ArgumentException($"A category code must be at most {MaxCodeLength} characters.", nameof(code));
        return normalized;
    }
}
