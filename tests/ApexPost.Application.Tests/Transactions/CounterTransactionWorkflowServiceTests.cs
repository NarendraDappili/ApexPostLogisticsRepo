using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Repositories;
using ApexPost.Application.Notifications;
using ApexPost.Application.Transactions;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Transactions;

public sealed class CounterTransactionWorkflowServiceTests
{
    [Fact]
    public async Task Checkout_creates_a_mixed_paid_basket_and_uses_configured_tax()
    {
        var fixture = new Fixture(stock: 5);
        var transaction = await fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2, services: [new("Shipping", 8m)], moneyOrders: []));

        Assert.Equal(CounterTransactionStatus.Paid, transaction.Status);
        Assert.Equal(2, transaction.Lines.Count);
        Assert.Equal(28m, transaction.Subtotal.Amount);
        Assert.Equal(2.80m, transaction.Tax.Amount);
        Assert.Equal(30.80m, transaction.Total.Amount);
        Assert.Equal(3, fixture.Inventory.Quantity);
    }

    [Fact]
    public async Task Checkout_rejects_insufficient_stock_without_changing_inventory()
    {
        var fixture = new Fixture(stock: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2)));
        Assert.Equal(1, fixture.Inventory.Quantity);
    }

    [Fact]
    public async Task Checkout_rejects_inactive_or_wrong_branch_clerks_and_closed_branches()
    {
        var fixture = new Fixture();
        fixture.Clerk.Deactivate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CheckoutAsync(fixture.Request()));
        fixture.Clerk.Activate();
        fixture.Clerk = new StaffMember(new("CLERK-B"), "Clerk B", StaffRole.CounterClerk, new("BR-B"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CheckoutAsync(fixture.Request()));
        fixture.Clerk = new StaffMember(new("CLERK-A"), "Clerk A", StaffRole.CounterClerk, new("BR-A"));
        fixture.Branch.Close();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CheckoutAsync(fixture.Request()));
    }

    [Fact]
    public async Task Checkout_requires_verified_same_context_money_order_when_above_threshold()
    {
        var fixture = new Fixture();
        fixture.MoneyOrders.Order = CreateMoneyOrder("MO-1", "TX-1", 101m, IdentityVerificationOutcome.Failed);
        await Assert.ThrowsAsync<IdentityVerificationRequiredException>(() => fixture.Service.CheckoutAsync(fixture.Request(moneyOrders: ["MO-1"])));

        fixture.MoneyOrders.Order = CreateMoneyOrder("MO-1", "TX-1", 101m, IdentityVerificationOutcome.Verified);
        var transaction = await fixture.Service.CheckoutAsync(fixture.Request(moneyOrders: ["MO-1"]));
        Assert.Contains(transaction.Lines, line => line.Description.Contains("MO-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Void_and_refund_require_assigned_active_branch_manager_and_reason_and_restore_stock()
    {
        var fixture = new Fixture(stock: 5);
        var paid = await fixture.Service.CheckoutAsync(fixture.Request());
        Assert.Equal(4, fixture.Inventory.Quantity);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.RefundAsync(paid.Id, new("MANAGER-A", " ")));
        fixture.Manager.Deactivate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RefundAsync(paid.Id, new("MANAGER-A", "customer request")));
        fixture.Manager.Activate();
        var refunded = await fixture.Service.RefundAsync(paid.Id, new("MANAGER-A", "customer request"));
        Assert.Equal(CounterTransactionStatus.Refunded, refunded.Status);
        Assert.Equal(5, fixture.Inventory.Quantity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.VoidAsync(paid.Id, new("MANAGER-A", "late void")));
    }

    [Fact]
    public async Task Void_transitions_an_open_transaction_only_with_manager_authorization()
    {
        var fixture = new Fixture();
        var open = new CounterTransaction(new("OPEN-1"), new("BR-A"), new("C-1"), new("CLERK-A"), [new(new ProductId("P-1"), "Envelope", 1, new(10m))], new(10m), new(1m), PaymentMethod.Cash);
        await fixture.Transactions.SaveAsync(open);

        var voided = await fixture.Service.VoidAsync(open.Id, new("MANAGER-A", "entry correction"));

        Assert.Equal(CounterTransactionStatus.Voided, voided.Status);
        Assert.Equal("entry correction", voided.StatusReason);
    }

    [Fact]
    public async Task Checkout_propagates_cancellation_without_changing_stock()
    {
        var fixture = new Fixture(stock: 5);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CheckoutAsync(fixture.Request(), cancellation.Token));
        Assert.Equal(5, fixture.Inventory.Quantity);
    }

    [Fact]
    public async Task Concurrent_checkouts_cannot_oversell_branch_stock_in_one_process()
    {
        var fixture = new Fixture(stock: 5);
        var results = await Task.WhenAll(Enumerable.Range(1, 10).Select(async index =>
        {
            try { await fixture.Service.CheckoutAsync(fixture.Request(transactionId: $"TX-{index}")); return true; }
            catch (InvalidOperationException) { return false; }
        }));

        Assert.Equal(5, results.Count(result => result));
        Assert.Equal(0, fixture.Inventory.Quantity);
    }

    [Fact]
    public async Task Void_and_refund_emit_simulated_notifications_and_delivery_failure_is_propagated()
    {
        var notifications = new RecordingNotificationSender();
        var fixture = new Fixture { Notifications = notifications };
        var paid = await fixture.Service.CheckoutAsync(fixture.Request());
        await fixture.Service.RefundAsync(paid.Id, new("MANAGER-A", "customer request"));
        Assert.Single(notifications.Messages);
        Assert.Contains("Refunded", notifications.Messages[0].Subject, StringComparison.Ordinal);

        var failingFixture = new Fixture { Notifications = new FailingNotificationSender() };
        var open = new CounterTransaction(new("OPEN-NOTIFY"), new("BR-A"), new("C-1"), new("CLERK-A"), [new(new ProductId("P-1"), "Envelope", 1, new(10m))], new(10m), new(1m), PaymentMethod.Cash);
        await failingFixture.Transactions.SaveAsync(open);
        await Assert.ThrowsAsync<IOException>(() => failingFixture.Service.VoidAsync(open.Id, new("MANAGER-A", "correction")));
        Assert.Equal(CounterTransactionStatus.Voided, (await failingFixture.Transactions.FindByIdAsync(open.Id))!.Status);
    }

    [Fact]
    public async Task Failed_transaction_save_compensates_previously_removed_inventory()
    {
        var fixture = new Fixture(stock: 5) { Transactions = new MemoryTransactionRepository { SaveException = new IOException("write failed") } };
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2)));
        Assert.Equal(5, fixture.Inventory.Quantity);
    }

    [Fact]
    public async Task Cancelled_transaction_save_still_compensates_previously_removed_inventory()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(stock: 5) { Transactions = new MemoryTransactionRepository { CancellationOnSave = cancellation } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2), cancellation.Token));

        Assert.Equal(5, fixture.Inventory.Quantity);
    }

    private static MoneyOrder CreateMoneyOrder(string id, string context, decimal amount, IdentityVerificationOutcome outcome)
    {
        var order = new MoneyOrder(new(id), new("BR-A"), new(amount), "Synthetic Sender", "Synthetic Recipient", new(2m), new(context));
        order.RecordIdentityVerification(new(new(context), "Demo ID", MaskedIdentityDocumentReference.FromDocumentNumber("ABCD1234"), outcome));
        return order;
    }

    private sealed class Fixture
    {
        public Branch Branch { get; } = new(new("BR-A"), "Branch A");
        public Counter Counter { get; } = new(new("C-1"), new("BR-A"));
        public StaffMember Clerk { get; set; } = new(new("CLERK-A"), "Clerk A", StaffRole.CounterClerk, new("BR-A"));
        public StaffMember Manager { get; } = new(new("MANAGER-A"), "Manager A", StaffRole.BranchManager, new("BR-A"));
        public MemoryInventoryRepository Inventory { get; }
        public MemoryMoneyOrderRepository MoneyOrders { get; } = new();
        public MemoryTransactionRepository Transactions { get; set; } = new();
        public INotificationSender? Notifications { get; set; }
        public CounterTransactionWorkflowService Service => new(new BranchRepo(Branch), new CounterRepo(Counter), new StaffRepo(() => [Clerk, Manager]), new ProductRepo(new Product(new("P-1"), "Envelope", new(10m))), Inventory, MoneyOrders, Transactions, new MoneyOrderPolicy(new(100m)), new FixedRateTaxPolicy(.1m), Notifications);

        public Fixture(int stock = 5) => Inventory = new MemoryInventoryRepository(new BranchInventoryItem(new("BR-A"), new("P-1"), stock));
        public CounterCheckoutRequest Request(int retailQuantity = 1, IReadOnlyCollection<ServiceBasketLineRequest>? services = null, IReadOnlyCollection<string>? moneyOrders = null, string transactionId = "TX-1") => new(transactionId, "BR-A", "C-1", Clerk.Id.Value, PaymentMethod.Cash, [new("P-1", retailQuantity)], services ?? [], moneyOrders ?? []);
    }

    private sealed class BranchRepo(Branch branch) : IBranchRepository { public Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken t = default) => Task.FromResult<Branch?>(code == branch.Code ? branch : null); public Task SaveAsync(Branch b, CancellationToken t = default) => Task.CompletedTask; }
    private sealed class CounterRepo(Counter counter) : ICounterRepository { public Task<Counter?> FindByCodeAsync(BranchCode b, CounterCode c, CancellationToken t = default) => Task.FromResult<Counter?>(b == counter.BranchCode && c == counter.Code ? counter : null); public Task SaveAsync(Counter c, CancellationToken t = default) => Task.CompletedTask; }
    private sealed class StaffRepo(Func<IReadOnlyCollection<StaffMember>> staff) : IStaffMemberRepository { public Task<StaffMember?> FindByIdAsync(StaffId id, CancellationToken t = default) => Task.FromResult(staff().FirstOrDefault(x => x.Id == id)); public Task SaveAsync(StaffMember s, CancellationToken t = default) => Task.CompletedTask; }
    private sealed class ProductRepo(Product product) : IProductRepository { public Task<Product?> FindByIdAsync(ProductId id, CancellationToken t = default) => Task.FromResult<Product?>(id == product.Id ? product : null); public Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<Product>>([product]); public Task SaveAsync(Product p, CancellationToken t = default) => Task.CompletedTask; }
    private sealed class MemoryMoneyOrderRepository : IMoneyOrderRepository { public MoneyOrder? Order { get; set; } public Task<MoneyOrder?> FindByIdAsync(MoneyOrderId id, CancellationToken t = default) => Task.FromResult(Order?.Id == id ? Order : null); public Task<IReadOnlyCollection<MoneyOrder>> FindByBranchAsync(BranchCode b, CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<MoneyOrder>>(Order is not null && Order.BranchCode == b ? [Order] : []); public Task SaveAsync(MoneyOrder m, CancellationToken t = default) { Order = m; return Task.CompletedTask; } }
    private sealed class MemoryTransactionRepository : ICounterTransactionRepository { private readonly Dictionary<string, CounterTransaction> _items = []; public Exception? SaveException { get; init; } public CancellationTokenSource? CancellationOnSave { get; init; } public Task<CounterTransaction?> FindByIdAsync(CounterTransactionId id, CancellationToken t = default) => Task.FromResult(_items.GetValueOrDefault(id.Value)); public Task<IReadOnlyCollection<CounterTransaction>> FindByBranchAsync(BranchCode b, CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<CounterTransaction>>(_items.Values.Where(x => x.BranchCode == b).ToArray()); public Task SaveAsync(CounterTransaction tx, CancellationToken t = default) { if (SaveException is not null) throw SaveException; CancellationOnSave?.Cancel(); t.ThrowIfCancellationRequested(); _items[tx.Id.Value] = tx; return Task.CompletedTask; } }
    private sealed class MemoryInventoryRepository(BranchInventoryItem item) : IInventoryRepository { private readonly SemaphoreSlim _gate = new(1, 1); public int Quantity => item.Quantity; public Task<BranchInventoryItem?> FindAsync(BranchCode b, ProductId p, CancellationToken t = default) => Task.FromResult<BranchInventoryItem?>(item); public Task<IReadOnlyCollection<BranchInventoryItem>> FindByBranchAsync(BranchCode b, CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<BranchInventoryItem>>(item.BranchCode == b ? [item] : []); public Task SaveAsync(BranchInventoryItem i, CancellationToken t = default) => Task.CompletedTask; public async Task<T> UpdateAsync<T>(BranchCode b, ProductId p, Func<BranchInventoryItem, T> update, CancellationToken t = default) { await _gate.WaitAsync(t); try { return update(item); } finally { _gate.Release(); } } }
    private sealed class RecordingNotificationSender : INotificationSender { public List<NotificationMessage> Messages { get; } = []; public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; } }
    private sealed class FailingNotificationSender : INotificationSender { public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) => Task.FromException(new IOException("simulated notification failure")); }
}
