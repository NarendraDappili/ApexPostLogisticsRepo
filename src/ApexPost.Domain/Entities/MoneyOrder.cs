using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Domain.Entities;

public sealed class MoneyOrder : IEquatable<MoneyOrder>
{
    public MoneyOrder(MoneyOrderId id, BranchCode branchCode, Money amount)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(amount);

        Id = id;
        BranchCode = branchCode;
        Amount = amount;
        IdentityVerificationOutcome = IdentityVerificationOutcome.Pending;
    }

    public MoneyOrderId Id { get; }

    public BranchCode BranchCode { get; }

    public Money Amount { get; }

    public IdentityVerificationOutcome IdentityVerificationOutcome { get; private set; }

    public void MarkIdentityVerified() => IdentityVerificationOutcome = IdentityVerificationOutcome.Verified;

    public void MarkIdentityVerificationFailed() => IdentityVerificationOutcome = IdentityVerificationOutcome.Failed;

    public bool Equals(MoneyOrder? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is MoneyOrder other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
