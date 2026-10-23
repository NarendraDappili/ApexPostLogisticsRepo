namespace ApexPost.Domain.ValueObjects;

/// <summary>Stores only a non-reversible masked document reference.</summary>
public sealed record MaskedIdentityDocumentReference
{
    private MaskedIdentityDocumentReference(string value) => Value = value;

    public string Value { get; }

    public static MaskedIdentityDocumentReference FromDocumentNumber(string documentNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);
        var normalized = documentNumber.Trim();
        var suffix = normalized.Length <= 4 ? string.Empty : normalized[^4..];
        return new MaskedIdentityDocumentReference($"****{suffix}");
    }

    public static MaskedIdentityDocumentReference FromMaskedValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!value.StartsWith("****", StringComparison.Ordinal))
        {
            throw new ArgumentException("A stored identity-document reference must be masked.", nameof(value));
        }

        return new MaskedIdentityDocumentReference(value);
    }

    public override string ToString() => Value;
}
