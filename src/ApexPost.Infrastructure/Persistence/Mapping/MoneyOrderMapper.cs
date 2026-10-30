using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence.Entities;

namespace ApexPost.Infrastructure.Persistence.Mapping;

/// <summary>
/// Maps the money-order aggregate to and from its persistence entities. Rebuilding goes through the domain
/// constructor and <see cref="MoneyOrder.RecordIdentityVerification"/>, so stored rows are re-validated,
/// and a stored reference that is not masked is rejected by <see cref="MaskedIdentityDocumentReference.FromMaskedValue"/>.
/// </summary>
public static class MoneyOrderMapper
{
    public static MoneyOrder ToDomain(MoneyOrderEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var order = new MoneyOrder(entity.MoneyOrderCode, entity.BranchCode, new Money(entity.Amount), entity.Sender, entity.Recipient, new Money(entity.Fee), entity.TransactionContextId);
        if (entity.IdentityVerification is { } verification)
        {
            order.RecordIdentityVerification(new IdentityVerificationRecord(
                order.TransactionContextId,
                verification.DocumentType,
                MaskedIdentityDocumentReference.FromMaskedValue(verification.MaskedDocumentReference),
                verification.Outcome));
        }

        return order;
    }

    public static MoneyOrderEntity ToEntity(MoneyOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var entity = new MoneyOrderEntity
        {
            MoneyOrderCode = order.Id,
            BranchCode = order.BranchCode,
            Amount = order.Amount.Amount,
            Fee = order.Fee.Amount,
            Sender = order.Sender,
            Recipient = order.Recipient,
            TransactionContextId = order.TransactionContextId,
        };
        Apply(order, entity);
        return entity;
    }

    /// <summary>
    /// Copies the only mutable part of the aggregate, its identity verification, onto a tracked entity.
    /// Amount, fee and parties are fixed once a money order is issued, so they are never copied back.
    /// </summary>
    public static void Apply(MoneyOrder order, MoneyOrderEntity entity)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(entity);
        if (order.IdentityVerification is not { } verification) return;

        entity.IdentityVerification ??= new IdentityVerificationEntity();
        entity.IdentityVerification.DocumentType = verification.DocumentType;
        entity.IdentityVerification.MaskedDocumentReference = verification.MaskedDocumentReference.Value;
        entity.IdentityVerification.Outcome = verification.Outcome;
    }
}
