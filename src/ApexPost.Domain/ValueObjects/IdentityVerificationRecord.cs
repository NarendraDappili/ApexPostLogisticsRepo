using ApexPost.Domain.Enums;

namespace ApexPost.Domain.ValueObjects;

/// <summary>Verification outcome with a document type and a masked reference only.</summary>
public sealed record IdentityVerificationRecord
{
    public IdentityVerificationRecord(
        MoneyOrderContextId contextId,
        string documentType,
        MaskedIdentityDocumentReference maskedDocumentReference,
        IdentityVerificationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(contextId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentNullException.ThrowIfNull(maskedDocumentReference);
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));

        ContextId = contextId;
        DocumentType = documentType.Trim();
        MaskedDocumentReference = maskedDocumentReference;
        Outcome = outcome;
    }

    public MoneyOrderContextId ContextId { get; }
    public string DocumentType { get; }
    public MaskedIdentityDocumentReference MaskedDocumentReference { get; }
    public IdentityVerificationOutcome Outcome { get; }
}
