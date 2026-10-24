using ApexPost.Application.Repositories;
using ApexPost.Application.Notifications;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.MoneyOrders;

/// <summary>Creates money orders and applies configured identity-verification policy.</summary>
public sealed class MoneyOrderWorkflowService
{
    private readonly IBranchRepository _branches;
    private readonly IMoneyOrderRepository _moneyOrders;
    private readonly IMoneyOrderPolicy _policy;
    private readonly INotificationSender? _notifications;

    public MoneyOrderWorkflowService(IBranchRepository branches, IMoneyOrderRepository moneyOrders, IMoneyOrderPolicy policy, INotificationSender? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(moneyOrders);
        ArgumentNullException.ThrowIfNull(policy);
        _branches = branches;
        _moneyOrders = moneyOrders;
        _policy = policy;
        _notifications = notifications;
    }

    public async Task<MoneyOrderCreationResult> CreateAsync(MoneyOrderCreationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var amount = new Money(request.Amount);
        if (amount.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(request.Amount), "Money-order amount must be positive.");
        var fee = new Money(request.Fee);
        var id = new MoneyOrderId(request.MoneyOrderId);
        var branchCode = new BranchCode(request.BranchCode);
        var contextId = new MoneyOrderContextId(request.TransactionContextId);

        var branch = await _branches.FindByCodeAsync(branchCode, cancellationToken).ConfigureAwait(false)
            ?? throw new MoneyOrderBranchNotFoundException("The money-order branch was not found.");
        if (!branch.CanAcceptTransactions) throw new InvalidOperationException("The money-order branch is closed.");
        if (await _moneyOrders.FindByIdAsync(id, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException("A money order with this identifier already exists.");
        }

        var moneyOrder = new MoneyOrder(id, branchCode, amount, request.Sender, request.Recipient, fee, contextId);
        if (request.IdentityVerification is not null)
        {
            moneyOrder.RecordIdentityVerification(ToVerificationRecord(request.IdentityVerification));
        }

        var requiresVerification = amount.Amount > _policy.HighValueThreshold.Amount;
        if (requiresVerification && moneyOrder.IdentityVerificationOutcome != IdentityVerificationOutcome.Verified)
        {
            throw new IdentityVerificationRequiredException("A high-value money order requires successful verification in the same transaction context.");
        }

        await _moneyOrders.SaveAsync(moneyOrder, cancellationToken).ConfigureAwait(false);
        if (requiresVerification && _notifications is not null)
        {
            await _notifications.SendAsync(new NotificationMessage("compliance-alerts@example.test", "ApexPost high-value money order", $"Money order {moneyOrder.Id.Value} at {moneyOrder.BranchCode.Value} was verified."), cancellationToken).ConfigureAwait(false);
        }
        return new MoneyOrderCreationResult(moneyOrder, moneyOrder.Total, requiresVerification);
    }

    private static IdentityVerificationRecord ToVerificationRecord(IdentityVerificationRequest verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (!Enum.IsDefined(verification.Outcome)) throw new ArgumentOutOfRangeException(nameof(verification.Outcome));
        return new IdentityVerificationRecord(
            new MoneyOrderContextId(verification.ContextId),
            verification.DocumentType,
            MaskedIdentityDocumentReference.FromDocumentNumber(verification.DocumentNumber),
            verification.Outcome);
    }
}
