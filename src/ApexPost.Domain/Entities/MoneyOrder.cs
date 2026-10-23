using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class MoneyOrder : IEquatable<MoneyOrder>
{
    public MoneyOrder(
        MoneyOrderId id,
        BranchCode branchCode,
        Money amount,
        string sender,
        string recipient,
        Money fee,
        MoneyOrderContextId transactionContextId)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(sender);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentNullException.ThrowIfNull(fee);
        ArgumentNullException.ThrowIfNull(transactionContextId);

        Id = id;
        BranchCode = branchCode;
        Amount = amount;
        Sender = sender.Trim();
        Recipient = recipient.Trim();
        Fee = fee;
        TransactionContextId = transactionContextId;
        IdentityVerificationOutcome = IdentityVerificationOutcome.Pending;
    }

    public MoneyOrderId Id { get; }

    public BranchCode BranchCode { get; }

    public Money Amount { get; }

    public string Sender { get; }

    public string Recipient { get; }

    public Money Fee { get; }

    public MoneyOrderContextId TransactionContextId { get; }

    public Money Total => new(checked(Amount.Amount + Fee.Amount));

    public IdentityVerificationOutcome IdentityVerificationOutcome { get; private set; }

    public IdentityVerificationRecord? IdentityVerification { get; private set; }

    public void RecordIdentityVerification(IdentityVerificationRecord verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (verification.ContextId != TransactionContextId)
        {
            throw new InvalidOperationException("Identity verification must belong to the same money-order context.");
        }

        IdentityVerification = verification;
        IdentityVerificationOutcome = verification.Outcome;
    }

    public bool Equals(MoneyOrder? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is MoneyOrder other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
