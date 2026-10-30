using ApexPost.Application.Abstractions;
using ApexPost.Application.Events;
using ApexPost.Application.Repositories;
using ApexPost.Application.Validation;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.MoneyOrders;

/// <summary>
/// Issues money orders with a policy-calculated fee and records identity verification as a separate step.
/// A high-value order can be issued while verification is pending, but checkout refuses to complete it
/// until a successful verification has been recorded in the same transaction context.
/// </summary>
public sealed class MoneyOrderWorkflowService
{
    private readonly IBranchRepository _branches;
    private readonly IMoneyOrderRepository _moneyOrders;
    private readonly IMoneyOrderPolicy _policy;
    private readonly IIdentityVerifier _identityVerifier;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventPublisher? _events;

    public MoneyOrderWorkflowService(IBranchRepository branches, IMoneyOrderRepository moneyOrders, IMoneyOrderPolicy policy, IIdentityVerifier identityVerifier, IUnitOfWork unitOfWork, IEventPublisher? events = null)
    {
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(moneyOrders);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(identityVerifier);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
        _branches = branches;
        _moneyOrders = moneyOrders;
        _policy = policy;
        _identityVerifier = identityVerifier;
        _events = events;
    }

    public IReadOnlyCollection<string> SupportedDocumentTypes => _identityVerifier.SupportedDocumentTypes;

    public async Task<MoneyOrderCreationResult> CreateAsync(MoneyOrderCreationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(request.Amount), "Money-order amount must be positive.");
        RequestValidator.Validate(request);
        var amount = new Money(request.Amount);
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

        var moneyOrder = new MoneyOrder(id, branchCode, amount, request.Sender, request.Recipient, _policy.CalculateFee(amount), contextId);
        var requiresVerification = RequiresVerification(moneyOrder);
        _moneyOrders.Add(moneyOrder);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (_events is not null)
        {
            // The order is committed, so its event must not be lost if the client disconnects now.
            await _events.PublishAsync(new MoneyOrderIssued(moneyOrder, requiresVerification), CancellationToken.None).ConfigureAwait(false);
        }

        return new MoneyOrderCreationResult(moneyOrder, moneyOrder.Total, requiresVerification);
    }

    public async Task<MoneyOrder> GetAsync(MoneyOrderId moneyOrderId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moneyOrderId);
        return await _moneyOrders.FindByIdAsync(moneyOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw new MoneyOrderNotFoundException("The money order was not found.");
    }

    public bool RequiresVerification(MoneyOrder moneyOrder)
    {
        ArgumentNullException.ThrowIfNull(moneyOrder);
        return moneyOrder.RequiresVerification(_policy.HighValueThreshold);
    }

    /// <summary>Asks the identity verifier for an outcome and records it, masked, against the order's transaction context.</summary>
    public async Task<MoneyOrder> VerifyIdentityAsync(MoneyOrderId moneyOrderId, BranchCode branchCode, IdentityVerificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(branchCode);
        ArgumentNullException.ThrowIfNull(request);
        RequestValidator.Validate(request);
        var moneyOrder = await GetAsync(moneyOrderId, cancellationToken).ConfigureAwait(false);
        if (moneyOrder.BranchCode != branchCode) throw new InvalidOperationException("The money order belongs to another branch.");
        if (moneyOrder.IdentityVerificationOutcome == IdentityVerificationOutcome.Verified)
        {
            throw new InvalidOperationException("Identity has already been verified for this money order.");
        }

        var outcome = await _identityVerifier.VerifyAsync(request.DocumentType.Trim(), request.DocumentNumber.Trim(), cancellationToken).ConfigureAwait(false);
        moneyOrder.RecordIdentityVerification(new IdentityVerificationRecord(
            moneyOrder.TransactionContextId,
            request.DocumentType,
            MaskedIdentityDocumentReference.FromDocumentNumber(request.DocumentNumber),
            outcome));
        _moneyOrders.Update(moneyOrder);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return moneyOrder;
    }
}
