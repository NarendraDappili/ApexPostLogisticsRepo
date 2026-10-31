using ApexPost.Application.Abstractions;
using ApexPost.Application.Events;
using ApexPost.Application.Inventory;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Pricing;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Transactions;

public sealed class CounterTransactionNotFoundException(string message) : NotFoundException(message);

/// <summary>
/// Coordinates a mixed counter basket. Each use case stages its stock and transaction changes and commits them
/// in one unit of work, so a failure at any point persists nothing. Two clerks selling the same last unit are
/// separated by optimistic concurrency at commit: the later commit fails with <see cref="ConcurrencyConflictException"/>.
/// </summary>
public sealed class CounterTransactionWorkflowService
{
    private readonly IBranchRepository _branches;
    private readonly ICounterRepository _counters;
    private readonly IStaffMemberRepository _staff;
    private readonly IProductRepository _products;
    private readonly InventoryService _inventory;
    private readonly IShipmentRepository _shipments;
    private readonly IShippingRateProvider _rates;
    private readonly IMoneyOrderRepository _moneyOrders;
    private readonly ICounterTransactionRepository _transactions;
    private readonly IMoneyOrderPolicy _moneyOrderPolicy;
    private readonly ITaxPolicy _taxPolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventPublisher? _events;

    public CounterTransactionWorkflowService(IBranchRepository branches, ICounterRepository counters, IStaffMemberRepository staff, IProductRepository products, InventoryService inventory, IShipmentRepository shipments, IShippingRateProvider rates, IMoneyOrderRepository moneyOrders, ICounterTransactionRepository transactions, IMoneyOrderPolicy moneyOrderPolicy, ITaxPolicy taxPolicy, IUnitOfWork unitOfWork, IEventPublisher? events = null)
    {
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        _staff = staff ?? throw new ArgumentNullException(nameof(staff));
        _products = products ?? throw new ArgumentNullException(nameof(products));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _shipments = shipments ?? throw new ArgumentNullException(nameof(shipments));
        _rates = rates ?? throw new ArgumentNullException(nameof(rates));
        _moneyOrders = moneyOrders ?? throw new ArgumentNullException(nameof(moneyOrders));
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _moneyOrderPolicy = moneyOrderPolicy ?? throw new ArgumentNullException(nameof(moneyOrderPolicy));
        _taxPolicy = taxPolicy ?? throw new ArgumentNullException(nameof(taxPolicy));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _events = events;
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
        if (request.RetailLines is null || request.ShipmentIds is null || request.MoneyOrderIds is null) throw new ArgumentException("Transaction line collections are required.");

        await ValidatePointOfSaleAsync(id, branchCode, counterCode, clerkId, cancellationToken).ConfigureAwait(false);
        var retailLines = await CreateRetailLinesAsync(request.RetailLines, cancellationToken).ConfigureAwait(false);
        var shipmentLines = await CreateShipmentLinesAsync(request.ShipmentIds, id, branchCode, cancellationToken).ConfigureAwait(false);
        var moneyOrderLines = await CreateMoneyOrderLinesAsync(request.MoneyOrderIds, id, branchCode, cancellationToken).ConfigureAwait(false);
        var lines = retailLines.Concat(shipmentLines).Concat(moneyOrderLines).ToArray();
        if (lines.Length == 0) throw new InvalidOperationException("A transaction requires at least one line.");

        var subtotal = lines.Select(line => line.Total).Total;
        var transaction = new CounterTransaction(id, branchCode, counterCode, clerkId, lines, subtotal, _taxPolicy.Calculate(subtotal), request.PaymentMethod);
        transaction.MarkPaid();

        var movements = new List<StockMovement>();
        foreach (var line in retailLines)
        {
            movements.Add(await _inventory.RemoveStockForSaleAsync(branchCode, line.ProductId!, line.Quantity, cancellationToken).ConfigureAwait(false));
        }

        _transactions.Add(transaction);
        // One commit: the stock decrements, the transaction, its lines and its payment succeed or fail together.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The sale is committed: raise its events without the request token, so a client disconnect
        // cannot drop a low-stock alert for stock that has already left the shelf.
        foreach (var movement in movements)
        {
            await _inventory.RaiseLowStockAlertAsync(movement, CancellationToken.None).ConfigureAwait(false);
        }

        if (_events is not null)
        {
            await _events.PublishAsync(new TransactionCompleted(transaction), CancellationToken.None).ConfigureAwait(false);
        }

        return transaction;
    }

    public async Task<CounterTransaction> VoidAsync(CounterTransactionId transactionId, ManagerTransactionActionRequest action, CancellationToken cancellationToken = default) =>
        await ReverseAsync(transactionId, action, refund: false, cancellationToken).ConfigureAwait(false);

    public async Task<CounterTransaction> RefundAsync(CounterTransactionId transactionId, ManagerTransactionActionRequest action, CancellationToken cancellationToken = default) =>
        await ReverseAsync(transactionId, action, refund: true, cancellationToken).ConfigureAwait(false);

    private async Task<CounterTransaction> ReverseAsync(CounterTransactionId transactionId, ManagerTransactionActionRequest action, bool refund, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transactionId); ArgumentNullException.ThrowIfNull(action);
        var transaction = await _transactions.FindByIdAsync(transactionId, cancellationToken).ConfigureAwait(false)
            ?? throw new CounterTransactionNotFoundException("The counter transaction was not found.");
        var manager = await _staff.FindByIdAsync(new StaffId(action.ManagerId), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The manager was not found.");
        if (!manager.IsActive || manager.Role != StaffRole.BranchManager || manager.AssignedBranchCode != transaction.BranchCode)
            throw new InvalidOperationException("A Branch Manager assigned to the transaction branch must authorize this action.");

        // Only a paid sale has taken stock off the shelf, so only a paid sale gives it back.
        var stockWasTaken = transaction.Status == CounterTransactionStatus.Paid;
        if (refund) transaction.Refund(manager.Id, action.Reason); else transaction.Void(manager.Id, action.Reason);
        _transactions.Update(transaction);
        if (stockWasTaken)
        {
            foreach (var line in transaction.Lines.Where(line => line.IsRetail))
            {
                await _inventory.ReturnStockForReversalAsync(transaction.BranchCode, line.ProductId!, line.Quantity, cancellationToken).ConfigureAwait(false);
            }
        }

        // The new status and the returned stock are committed together, so a reversal can no longer
        // be persisted with its stock still missing.
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_events is not null)
        {
            ApexPostEvent reversal = refund ? new TransactionRefunded(transaction) : new TransactionVoided(transaction);
            await _events.PublishAsync(reversal, CancellationToken.None).ConfigureAwait(false);
        }

        return transaction;
    }

    private async Task ValidatePointOfSaleAsync(CounterTransactionId id, BranchCode branchCode, CounterCode counterCode, StaffId clerkId, CancellationToken cancellationToken)
    {
        // Sequential on purpose: the repositories share one unit of work (one DbContext per request),
        // which does not allow concurrent operations.
        var branch = await _branches.FindByCodeAsync(branchCode, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The transaction branch was not found.");
        if (!branch.CanAcceptTransactions) throw new InvalidOperationException("The transaction branch is closed.");
        var counter = await _counters.FindByCodeAsync(branchCode, counterCode, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The transaction counter was not found.");
        if (!counter.IsOpen) throw new InvalidOperationException("The transaction counter is closed.");
        var clerk = await _staff.FindByIdAsync(clerkId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("The transaction clerk was not found.");
        if (clerk.Role != StaffRole.CounterClerk || !clerk.CanProcessTransactionsAt(branchCode)) throw new InvalidOperationException("An active Counter Clerk assigned to the branch is required.");
        if (await _transactions.FindByIdAsync(id, cancellationToken).ConfigureAwait(false) is not null) throw new InvalidOperationException("A transaction with this identifier already exists.");
    }

    private async Task<IReadOnlyCollection<CounterTransactionLine>> CreateRetailLinesAsync(IEnumerable<RetailBasketLineRequest> requests, CancellationToken cancellationToken)
    {
        var lines = new List<CounterTransactionLine>();
        foreach (var request in requests)
        {
            ArgumentNullException.ThrowIfNull(request);
            var productId = new ProductId(request.ProductId);
            var product = await _products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException($"Retail product {productId.Value} was not found.");
            var total = RetailPriceCalculator.Calculate(product, request.Quantity);
            lines.Add(new CounterTransactionLine(product.Id, product.Name, request.Quantity, new Money(total.Amount / request.Quantity)));
        }
        return lines;
    }

    private async Task<IReadOnlyCollection<CounterTransactionLine>> CreateShipmentLinesAsync(IEnumerable<string> ids, CounterTransactionId transactionId, BranchCode branchCode, CancellationToken cancellationToken)
    {
        var lines = new List<CounterTransactionLine>();
        IReadOnlyCollection<ShippingRate>? rates = null;
        foreach (var rawId in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var shipment = await _shipments.FindByIdAsync(new ShipmentId(rawId), cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("A shipment was not found.");
            // The shipment is bound to one transaction ID at creation, so it can never be billed twice.
            if (shipment.OriginBranchCode != branchCode || shipment.TransactionId != transactionId) throw new InvalidOperationException("Shipment must belong to this branch and transaction.");
            rates ??= await _rates.GetRatesAsync(cancellationToken).ConfigureAwait(false);
            var price = ShippingPriceCalculator.Calculate(new ShippingPricingRequest(shipment, shipment.ShipmentType, shipment.ServiceLevel), rates);
            lines.Add(new CounterTransactionLine(null, $"{shipment.ServiceLevel} {shipment.ShipmentType.ToString().ToLowerInvariant()} {shipment.Id.Value} to {shipment.DestinationZoneCode.Value}", 1, price));
        }
        return lines;
    }

    private async Task<IReadOnlyCollection<CounterTransactionLine>> CreateMoneyOrderLinesAsync(IEnumerable<string> ids, CounterTransactionId transactionId, BranchCode branchCode, CancellationToken cancellationToken)
    {
        var lines = new List<CounterTransactionLine>();
        foreach (var rawId in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var order = await _moneyOrders.FindByIdAsync(new MoneyOrderId(rawId), cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("A money order was not found.");
            if (order.BranchCode != branchCode || order.TransactionContextId.Value != transactionId.Value) throw new InvalidOperationException("Money order must belong to this branch and transaction context.");
            if (!order.IsClearedForPayment(_moneyOrderPolicy.HighValueThreshold)) throw new IdentityVerificationRequiredException("A high-value money order requires successful same-transaction verification.");
            lines.Add(new CounterTransactionLine(null, $"Money order {order.Id.Value}", 1, order.Total));
        }
        return lines;
    }
}
