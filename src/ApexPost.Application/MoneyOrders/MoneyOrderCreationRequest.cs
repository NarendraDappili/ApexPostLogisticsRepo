using ApexPost.Domain.Enums;

namespace ApexPost.Application.MoneyOrders;

/// <summary>Raw input for a money order. The document number is used only to create a masked reference.</summary>
public sealed record MoneyOrderCreationRequest(
    string MoneyOrderId,
    string BranchCode,
    decimal Amount,
    string Sender,
    string Recipient,
    decimal Fee,
    string TransactionContextId,
    IdentityVerificationRequest? IdentityVerification);

public sealed record IdentityVerificationRequest(
    string ContextId,
    string DocumentType,
    string DocumentNumber,
    IdentityVerificationOutcome Outcome);
