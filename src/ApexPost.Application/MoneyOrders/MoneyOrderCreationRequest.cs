using ApexPost.Application.Validation;

namespace ApexPost.Application.MoneyOrders;

/// <summary>Raw input for a money order. The fee is calculated by policy and never supplied by the clerk.</summary>
public sealed record MoneyOrderCreationRequest(
    [property: NotBlank(48)] string MoneyOrderId,
    [property: NotBlank(48)] string BranchCode,
    decimal Amount,
    [property: NotBlank(160)] string Sender,
    [property: NotBlank(160)] string Recipient,
    [property: NotBlank(48)] string TransactionContextId);

/// <summary>
/// Identity document presented for verification. The outcome is decided by <see cref="IIdentityVerifier"/>,
/// and the document number is used only to create a masked reference.
/// </summary>
public sealed record IdentityVerificationRequest(
    [property: NotBlank(80)] string DocumentType,
    [property: NotBlank(128)] string DocumentNumber);
