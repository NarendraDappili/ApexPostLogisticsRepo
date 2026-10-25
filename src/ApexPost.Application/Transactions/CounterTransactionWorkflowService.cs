using System.Collections.Concurrent;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Pricing;
using ApexPost.Application.Repositories;
using ApexPost.Application.Notifications;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Transactions;

/// <summary>Coordinates a mixed counter basket using existing pricing and repository abstractions.</summary>
public sealed class CounterTransactionWorkflowService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BranchLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly IBranchRepository _branches;
    private readonly ICounterRepository _counters;
    private readonly IStaffMemberRepository _staff;
    private readonly IProductRepository _products;
    private readonly IInventoryRepository _inventory;
    private readonly IMoneyOrderRepository _moneyOrders;
    private readonly ICounterTransactionRepository _transactions;
    private readonly IMoneyOrderPolicy _moneyOrderPolicy;
    private readonly ITaxPolicy _taxPolicy;
    private readonly INotificationSender? _notifications;

    public CounterTransactionWorkflowService(IBranchRepository branches, ICounterRepository counters, IStaffMemberRepository staff, IProductRepository products, IInventoryRepository inventory, IMoneyOrderRepository moneyOrders, ICounterTransactionRepository transactions, IMoneyOrderPolicy moneyOrderPolicy, ITaxPolicy taxPolicy, INotificationSender? notifications = null)
    {
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        _staff = staff ?? throw new ArgumentNullException(nameof(staff));
        _products = products ?? throw new ArgumentNullException(nameof(products));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _moneyOrders = moneyOrders ?? throw new ArgumentNullException(nameof(moneyOrders));
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _moneyOrderPolicy = moneyOrderPolicy ?? throw new ArgumentNullException(nameof(moneyOrderPolicy));
        _taxPolicy = taxPolicy ?? throw new ArgumentNullException(nameof(taxPolicy));
        _notifications = notifications;
    }

    public async Task<CounterTransaction> CheckoutAsync(CounterCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var id = new CounterTransactionId(request.TransactionId);
        var branchCode = new BranchCode(request.BranchCode);
        var counterCode = new CounterCode(request.CounterCode);
        var clerkId = new StaffId(request.ClerkId);
        if (!Enum.IsDefined(request.PaymentMethod)) throw new ArgumentOutOfRangeException(nameof(request.PaymentMethod));
        if (request.RetailLines is null || request.ServiceLines is null || request.MoneyOrderIds is null) throw new ArgumentException("Transaction line collections are required.");

        var gate = BranchLocks.GetOrAdd(branchCode.Value, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ValidatePointOfSaleAsync(id, branchCode, counterCode, clerkId, cancellationToken).ConfigureAwait(false);
            var retailLines = await CreateRetailLinesAsync(request.RetailLines, cancellationToken).ConfigureAwait(false);
            var serviceLines = CreateServiceLines(request.ServiceLines);
            var moneyOrderLines = await CreateMoneyOrderLinesAsync(request.MoneyOrderIds, id, branchCode, cancellationToken).ConfigureAwait(false);
            var lines = retailLines.Concat(serviceLines).Concat(moneyOrderLines).ToArray();
            if (lines.Length == 0) throw new InvalidOperationException("A transaction requires at least one line.");
            var subtotal = new Money(lines.Sum(line => line.Total.Amount));
            var transaction = new CounterTransaction(id, branchCode, counterCode, clerkId, lines, subtotal, _taxPolicy.Calculate(subtotal), request.PaymentMethod);
            transaction.MarkPaid();

            var removed = new List<CounterTransactionLine>();
            try
            {
                foreach (var line in retailLines)
                {
                    await RemoveStockAsync(branchCode, line.ProductId!, line.Quantity, cancellationToken).ConfigureAwait(false);
                    removed.Add(line);
                }
                await _transactions.SaveAsync(transaction, cancellationToken).ConfigureAwait(false);
                return transaction;
            }
            catch (Exception failure)
            {
                var compensationFailures = new List<Exception>();
                // Once inventory has changed, rollback must outlive request cancellation.
                foreach (var line in removed.AsEnumerable().Reverse())
                {
                    try { await AddStockAsync(branchCode, line.ProductId!, line.Quantity, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception compensationFailure) { compensationFailures.Add(compensationFailure); }
                }
                if (compensationFailures.Count > 0) throw new AggregateException("Checkout failed and inventory compensation was incomplete.", [failure, .. compensationFailures]);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task<CounterTransaction> VoidAsync(CounterTransactionId transactionId, ManagerTransactionActionRequest action, CancellationToken cancellationToken = default) =>
        await ReverseAsync(transactionId, action, refund: false, cancellationToken).ConfigureAwait(false);

    public async Task<CounterTransaction> RefundAsync(CounterTransactionId transactionId, ManagerTransactionActionRequest action, CancellationToken cancellationToken = default) =>
        await ReverseAsync(transactionId, action, refund: true, cancellationToken).ConfigureAwait(false);

    private async Task<CounterTransaction> ReverseAsync(CounterTransactionId transactionId, ManagerTransactionActionRequest action, bool refund, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transactionId); ArgumentNullException.ThrowIfNull(action);
        var transaction = await _transactions.FindByIdAsync(transactionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The counter transaction was not found.");
        var gate = BranchLocks.GetOrAdd(transaction.BranchCode.Value, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manager = await _staff.FindByIdAsync(new StaffId(action.ManagerId), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The manager was not found.");
            if (!manager.IsActive || manager.Role != StaffRole.BranchManager || manager.AssignedBranchCode != transaction.BranchCode)
                throw new InvalidOperationException("A Branch Manager assigned to the transaction branch must authorize this action.");
            if (refund) transaction.Refund(manager.Id, action.Reason); else transaction.Void(manager.Id, action.Reason);
            await _transactions.SaveAsync(transaction, cancellationToken).ConfigureAwait(false);
            if (refund)
            {
                try
                {
                    // The refund status is persisted; finish stock restoration even if the client disconnects.
                    foreach (var line in transaction.Lines.Where(line => line.IsRetail))
                    {
                        await AddStockAsync(transaction.BranchCode, line.ProductId!, line.Quantity, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception failure)
                {
                    // The state was already persisted, so report this explicitly rather than claim atomic reversal.
                    throw new InvalidOperationException("Transaction status was persisted but inventory restoration failed; reconciliation is required.", failure);
                }
            }
            if (_notifications is not null)
            {
                await _notifications.SendAsync(new NotificationMessage("operations-alerts@example.test", $"ApexPost transaction {transaction.Status}", $"Transaction {transaction.Id.Value} at {transaction.BranchCode.Value} was {transaction.Status.ToString().ToLowerInvariant()}."), cancellationToken).ConfigureAwait(false);
            }
            return transaction;
        }
        finally { gate.Release(); }
    }

    private async Task ValidatePointOfSaleAsync(CounterTransactionId id, BranchCode branchCode, CounterCode counterCode, StaffId clerkId, CancellationToken cancellationToken)
    {
        // These repository reads address separate aggregates/files and have no ordering dependency.
        // Keep mutations below the branch gate; only this validation fan-out is safe to parallelize.
        var branchTask = _branches.FindByCodeAsync(branchCode, cancellationToken);
        var counterTask = _counters.FindByCodeAsync(branchCode, counterCode, cancellationToken);
        var clerkTask = _staff.FindByIdAsync(clerkId, cancellationToken);
        var existingTransactionTask = _transactions.FindByIdAsync(id, cancellationToken);
        await Task.WhenAll(branchTask, counterTask, clerkTask, existingTransactionTask).ConfigureAwait(false);

        var branch = await branchTask.ConfigureAwait(false) ?? throw new InvalidOperationException("The transaction branch was not found.");
        if (!branch.CanAcceptTransactions) throw new InvalidOperationException("The transaction branch is closed.");
        var counter = await counterTask.ConfigureAwait(false) ?? throw new InvalidOperationException("The transaction counter was not found.");
        if (!counter.IsOpen) throw new InvalidOperationException("The transaction counter is closed.");
        var clerk = await clerkTask.ConfigureAwait(false) ?? throw new InvalidOperationException("The transaction clerk was not found.");
        if (clerk.Role != StaffRole.CounterClerk || !clerk.CanProcessTransactionsAt(branchCode)) throw new InvalidOperationException("An active Counter Clerk assigned to the branch is required.");
        if (await existingTransactionTask.ConfigureAwait(false) is not null) throw new InvalidOperationException("A transaction with this identifier already exists.");
    }

    private async Task<IReadOnlyCollection<CounterTransactionLine>> CreateRetailLinesAsync(IEnumerable<RetailBasketLineRequest> requests, CancellationToken cancellationToken)
    {
        var lines = new List<CounterTransactionLine>();
        foreach (var request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            var productId = new ProductId(request.ProductId);
            var product = await _products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("A retail product was not found.");
            var total = RetailPriceCalculator.Calculate(product, request.Quantity);
            lines.Add(new CounterTransactionLine(product.Id, product.Name, request.Quantity, new Money(total.Amount / request.Quantity)));
        }
        return lines;
    }

    private static IReadOnlyCollection<CounterTransactionLine> CreateServiceLines(IEnumerable<ServiceBasketLineRequest> requests) => requests.Select(request =>
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CounterTransactionLine(null, request.Description, request.Quantity, new Money(request.UnitPrice));
    }).ToArray();

    private async Task<IReadOnlyCollection<CounterTransactionLine>> CreateMoneyOrderLinesAsync(IEnumerable<string> ids, CounterTransactionId transactionId, BranchCode branchCode, CancellationToken cancellationToken)
    {
        var lines = new List<CounterTransactionLine>();
        foreach (var rawId in ids)
        {
            var order = await _moneyOrders.FindByIdAsync(new MoneyOrderId(rawId), cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("A money order was not found.");
            if (order.BranchCode != branchCode || order.TransactionContextId.Value != transactionId.Value) throw new InvalidOperationException("Money order must belong to this branch and transaction context.");
            if (order.Amount.Amount > _moneyOrderPolicy.HighValueThreshold.Amount && order.IdentityVerificationOutcome != IdentityVerificationOutcome.Verified) throw new IdentityVerificationRequiredException("A high-value money order requires successful same-transaction verification.");
            lines.Add(new CounterTransactionLine(null, $"Money order {order.Id.Value}", 1, order.Total));
        }
        return lines;
    }

    private async Task RemoveStockAsync(BranchCode branch, ProductId product, int quantity, CancellationToken token) =>
        await _inventory.UpdateAsync(branch, product, item => { item.RemoveStock(quantity); return 0; }, token).ConfigureAwait(false);
    private async Task AddStockAsync(BranchCode branch, ProductId product, int quantity, CancellationToken token) =>
        await _inventory.UpdateAsync(branch, product, item => { item.AddStock(quantity); return 0; }, token).ConfigureAwait(false);
}
