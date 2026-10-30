using ApexPost.Application.Events;
using ApexPost.Application.Inventory;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Pricing;
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
    public async Task Checkout_creates_a_mixed_paid_basket_priced_on_the_server_and_uses_configured_tax()
    {
        var fixture = new Fixture(stock: 5);
        fixture.Shipments.Items.Add(Shipment("SHP-1", "TX-1"));
        var transaction = await fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2, shipments: ["SHP-1"]));

        Assert.Equal(CounterTransactionStatus.Paid, transaction.Status);
        Assert.Equal(2, transaction.Lines.Count);
        Assert.Contains(transaction.Lines, line => !line.IsRetail && line.UnitPrice.Amount == 8m && line.Description.Contains("SHP-1", StringComparison.Ordinal));
        Assert.Equal(28m, transaction.Subtotal.Amount);
        Assert.Equal(2.80m, transaction.Tax.Amount);
        Assert.Equal(30.80m, transaction.Total.Amount);
        Assert.Equal(3, fixture.Inventory.Quantity);
    }

    [Fact]
    public async Task Checkout_accepts_several_retail_lines_in_one_basket()
    {
        var fixture = new Fixture(stock: 10);
        var request = fixture.Request() with { RetailLines = [new("P-1", 2), new("P-1", 3)] };

        var transaction = await fixture.Service.CheckoutAsync(request);

        Assert.Equal(2, transaction.Lines.Count(line => line.IsRetail));
        Assert.Equal(50m, transaction.Subtotal.Amount);
        Assert.Equal(5, fixture.Inventory.Quantity);
    }

    [Fact]
    public async Task Checkout_rejects_shipments_from_another_transaction_or_branch_and_bills_a_shipment_once()
    {
        var fixture = new Fixture();
        fixture.Shipments.Items.Add(Shipment("SHP-OTHER", "TX-OTHER"));
        fixture.Shipments.Items.Add(Shipment("SHP-1", "TX-1"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CheckoutAsync(fixture.Request(shipments: ["SHP-OTHER"])));
        var transaction = await fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 0, shipments: ["SHP-1", "shp-1"]));

        Assert.Single(transaction.Lines);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 0, shipments: ["SHP-1"], transactionId: "TX-2")));
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
    public async Task Void_cancels_a_paid_sale_and_returns_its_stock()
    {
        var fixture = new Fixture(stock: 5);
        var paid = await fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 3));
        Assert.Equal(2, fixture.Inventory.Quantity);

        var voided = await fixture.Service.VoidAsync(paid.Id, new("MANAGER-A", "keyed in error"));

        Assert.Equal(CounterTransactionStatus.Voided, voided.Status);
        Assert.Equal("keyed in error", voided.StatusReason);
        Assert.Equal(5, fixture.Inventory.Quantity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RefundAsync(paid.Id, new("MANAGER-A", "second reversal")));
        Assert.Equal(5, fixture.Inventory.Quantity);
    }

    [Fact]
    public async Task Void_transitions_an_open_transaction_without_returning_stock_it_never_took()
    {
        var fixture = new Fixture();
        var open = new CounterTransaction(new("OPEN-1"), new("BR-A"), new("C-1"), new("CLERK-A"), [new(new ProductId("P-1"), "Envelope", 1, new(10m))], new(10m), new(1m), PaymentMethod.Cash);
        fixture.Transactions.Add(open);

        var voided = await fixture.Service.VoidAsync(open.Id, new("MANAGER-A", "entry correction"));

        Assert.Equal(CounterTransactionStatus.Voided, voided.Status);
        Assert.Equal("entry correction", voided.StatusReason);
        Assert.Equal(5, fixture.Inventory.Quantity);
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
    public async Task A_sale_that_crosses_the_low_stock_threshold_raises_a_low_stock_alert_and_a_completed_event()
    {
        var notifications = new RecordingNotificationSender();
        var observer = new RecordingObserver();
        var fixture = new Fixture(stock: 4) { Notifications = notifications, ExtraObserver = observer };

        await fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2));

        var alert = Assert.Single(notifications.Messages);
        Assert.Contains("low-stock", alert.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(observer.Events, applicationEvent => applicationEvent is StockLow { Quantity: 2 });
        Assert.Contains(observer.Events, applicationEvent => applicationEvent is TransactionCompleted);
    }

    [Fact]
    public async Task Void_and_refund_emit_simulated_notifications_and_delivery_failure_is_propagated()
    {
        var notifications = new RecordingNotificationSender();
        var fixture = new Fixture(stock: 50) { Notifications = notifications };
        var paid = await fixture.Service.CheckoutAsync(fixture.Request());
        await fixture.Service.RefundAsync(paid.Id, new("MANAGER-A", "customer request"));
        Assert.Single(notifications.Messages);
        Assert.Contains("Refunded", notifications.Messages[0].Subject, StringComparison.Ordinal);

        var failingFixture = new Fixture { Notifications = new FailingNotificationSender() };
        var open = new CounterTransaction(new("OPEN-NOTIFY"), new("BR-A"), new("C-1"), new("CLERK-A"), [new(new ProductId("P-1"), "Envelope", 1, new(10m))], new(10m), new(1m), PaymentMethod.Cash);
        failingFixture.Transactions.Add(open);
        await Assert.ThrowsAsync<IOException>(() => failingFixture.Service.VoidAsync(open.Id, new("MANAGER-A", "correction")));
        Assert.Equal(CounterTransactionStatus.Voided, (await failingFixture.Transactions.FindByIdAsync(open.Id))!.Status);
    }

    // Week 2 compensated stock by hand after a failed save. With a unit of work the stock change and the sale are
    // one commit, so these tests prove the commit is attempted once and that nothing is raised when it fails.
    // Proof that the database rows really roll back together belongs to the SQL Server integration tests.
    [Fact]
    public async Task Failed_commit_persists_nothing_and_raises_no_sale_events()
    {
        var observer = new RecordingObserver();
        var fixture = new Fixture(stock: 3) { UnitOfWork = new SpyUnitOfWork { FailWith = new IOException("write failed") }, ExtraObserver = observer };

        await Assert.ThrowsAsync<IOException>(() => fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2)));

        Assert.Equal((1, 0), (fixture.UnitOfWork.Attempts, fixture.UnitOfWork.Commits));
        Assert.Empty(observer.Events);
    }

    [Fact]
    public async Task Cancelled_commit_persists_nothing_and_raises_no_sale_events()
    {
        using var cancellation = new CancellationTokenSource();
        var observer = new RecordingObserver();
        var fixture = new Fixture(stock: 3) { UnitOfWork = new SpyUnitOfWork { CancelOnCommit = cancellation }, ExtraObserver = observer };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2), cancellation.Token));

        Assert.Equal(0, fixture.UnitOfWork.Commits);
        Assert.Empty(observer.Events);
    }

    [Fact]
    public async Task Checkout_and_reversal_each_commit_exactly_once()
    {
        var fixture = new Fixture(stock: 5);
        fixture.Shipments.Items.Add(Shipment("SHP-1", "TX-1"));

        var paid = await fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2, shipments: ["SHP-1"]));
        Assert.Equal(1, fixture.UnitOfWork.Commits);

        await fixture.Service.VoidAsync(paid.Id, new("MANAGER-A", "keyed in error"));
        Assert.Equal(2, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Rejected_checkout_or_reversal_never_commits()
    {
        var fixture = new Fixture(stock: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CheckoutAsync(fixture.Request(retailQuantity: 2)));
        await Assert.ThrowsAsync<CounterTransactionNotFoundException>(() => fixture.Service.RefundAsync(new("TX-404"), new("MANAGER-A", "no such sale")));

        Assert.Equal(0, fixture.UnitOfWork.Attempts);
    }

    private static MoneyOrder CreateMoneyOrder(string id, string context, decimal amount, IdentityVerificationOutcome outcome)
    {
        var order = new MoneyOrder(new(id), new("BR-A"), new(amount), "Synthetic Sender", "Synthetic Recipient", new(2m), new(context));
        order.RecordIdentityVerification(new(new(context), "Demo ID", MaskedIdentityDocumentReference.FromDocumentNumber("ABCD1234"), outcome));
        return order;
    }

    private static Shipment Shipment(string id, string transactionId) =>
        new(new(id), new("BR-A"), new("ZONE-A"), new(1m), new(10m, 10m, 1m), ShipmentType.Parcel, ShippingServiceLevel.Standard, new(transactionId));

    private sealed class Fixture
    {
        public Branch Branch { get; } = new(new("BR-A"), "Branch A");
        public Counter Counter { get; } = new(new("C-1"), new("BR-A"));
        public StaffMember Clerk { get; set; } = new(new("CLERK-A"), "Clerk A", StaffRole.CounterClerk, new("BR-A"));
        public StaffMember Manager { get; } = new(new("MANAGER-A"), "Manager A", StaffRole.BranchManager, new("BR-A"));
        public MemoryInventoryRepository Inventory { get; }
        public MemoryShipmentRepository Shipments { get; } = new();
        public MemoryMoneyOrderRepository MoneyOrders { get; } = new();
        public MemoryTransactionRepository Transactions { get; set; } = new();
        public SpyUnitOfWork UnitOfWork { get; set; } = new();
        public INotificationSender? Notifications { get; set; }
        public IEventObserver? ExtraObserver { get; set; }
        public CounterTransactionWorkflowService Service
        {
            get
            {
                IEventObserver[] observers = [.. new IEventObserver?[] { Notifications is null ? null : new NotificationEventObserver(Notifications, new NotificationRecipients()), ExtraObserver }.OfType<IEventObserver>()];
                var events = observers.Length == 0 ? null : new EventPublisher(observers);
                return new(new BranchRepo(Branch), new CounterRepo(Counter), new StaffRepo(() => [Clerk, Manager]), new ProductRepo(new Product(new("P-1"), "Envelope", new(10m))),
                    new InventoryService(Inventory, UnitOfWork, new InventoryServiceOptions { LowStockThreshold = 2 }, events), Shipments,
                    new RateProvider([new ShippingRate(ShipmentType.Parcel, ShippingServiceLevel.Standard, new ZoneCode("ZONE-A"), new Money(8m), new ParcelWeight(5m))]),
                    MoneyOrders, Transactions, new MoneyOrderPolicy(new(100m)), new FixedRateTaxPolicy(.1m), UnitOfWork, events);
            }
        }

        public Fixture(int stock = 5) => Inventory = new MemoryInventoryRepository(new BranchInventoryItem(new("BR-A"), new("P-1"), stock));
        public CounterCheckoutRequest Request(int retailQuantity = 1, IReadOnlyCollection<string>? shipments = null, IReadOnlyCollection<string>? moneyOrders = null, string transactionId = "TX-1") =>
            new(transactionId, "BR-A", "C-1", Clerk.Id.Value, PaymentMethod.Cash, retailQuantity > 0 ? [new("P-1", retailQuantity)] : [], shipments ?? [], moneyOrders ?? []);
    }

    private sealed class BranchRepo(Branch branch) : IBranchRepository { public Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken t = default) => Task.FromResult<Branch?>(code == branch.Code ? branch : null); public Task<IReadOnlyCollection<Branch>> GetAllAsync(CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<Branch>>([branch]); public void Add(Branch b) { } public void Update(Branch b) { } }
    private sealed class CounterRepo(Counter counter) : ICounterRepository { public Task<Counter?> FindByCodeAsync(BranchCode b, CounterCode c, CancellationToken t = default) => Task.FromResult<Counter?>(b == counter.BranchCode && c == counter.Code ? counter : null); public Task<IReadOnlyCollection<Counter>> FindByBranchAsync(BranchCode b, CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<Counter>>(b == counter.BranchCode ? [counter] : []); public void Add(Counter c) { } }
    private sealed class StaffRepo(Func<IReadOnlyCollection<StaffMember>> staff) : IStaffMemberRepository { public Task<StaffMember?> FindByIdAsync(StaffId id, CancellationToken t = default) => Task.FromResult(staff().FirstOrDefault(x => x.Id == id)); public Task<IReadOnlyCollection<StaffMember>> GetAllAsync(CancellationToken t = default) => Task.FromResult(staff()); public void Add(StaffMember s) { } public void Update(StaffMember s) { } }
    private sealed class ProductRepo(Product product) : IProductRepository { public Task<Product?> FindByIdAsync(ProductId id, CancellationToken t = default) => Task.FromResult<Product?>(id == product.Id ? product : null); public Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<Product>>([product]); }
    private sealed class RateProvider(IReadOnlyCollection<ShippingRate> rates) : IShippingRateProvider { public ValueTask<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken t = default) => ValueTask.FromResult(rates); }
    private sealed class MemoryShipmentRepository : IShipmentRepository { public List<Shipment> Items { get; } = []; public Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken t = default) => Task.FromResult(Items.FirstOrDefault(x => x.Id == id)); public Task<IReadOnlyCollection<Shipment>> FindByOriginBranchAsync(BranchCode b, CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<Shipment>>(Items.Where(x => x.OriginBranchCode == b).ToArray()); public void Add(Shipment s) => Items.Add(s); }
    private sealed class MemoryMoneyOrderRepository : IMoneyOrderRepository { public MoneyOrder? Order { get; set; } public Task<MoneyOrder?> FindByIdAsync(MoneyOrderId id, CancellationToken t = default) => Task.FromResult(Order?.Id == id ? Order : null); public Task<IReadOnlyCollection<MoneyOrder>> FindByBranchAsync(BranchCode b, CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<MoneyOrder>>(Order is not null && Order.BranchCode == b ? [Order] : []); public void Add(MoneyOrder m) => Order = m; public void Update(MoneyOrder m) => Order = m; }
    private sealed class MemoryTransactionRepository : ICounterTransactionRepository { private readonly Dictionary<string, CounterTransaction> _items = []; public Task<CounterTransaction?> FindByIdAsync(CounterTransactionId id, CancellationToken t = default) => Task.FromResult(_items.GetValueOrDefault(id.Value)); public Task<IReadOnlyCollection<CounterTransaction>> FindByBranchAsync(BranchCode b, CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<CounterTransaction>>(_items.Values.Where(x => x.BranchCode == b).ToArray()); public void Add(CounterTransaction tx) => _items[tx.Id.Value] = tx; public void Update(CounterTransaction tx) => _items[tx.Id.Value] = tx; }
    private sealed class MemoryInventoryRepository(BranchInventoryItem item) : IInventoryRepository { public int Quantity => item.Quantity; public Task<BranchInventoryItem?> FindAsync(BranchCode b, ProductId p, CancellationToken t = default) => Task.FromResult<BranchInventoryItem?>(item); public Task<IReadOnlyCollection<BranchInventoryItem>> FindByBranchAsync(BranchCode b, CancellationToken t = default) => Task.FromResult<IReadOnlyCollection<BranchInventoryItem>>(item.BranchCode == b ? [item] : []); public void Update(BranchInventoryItem i) { } }
    private sealed class RecordingNotificationSender : INotificationSender { public List<NotificationMessage> Messages { get; } = []; public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.CompletedTask; } }
    private sealed class FailingNotificationSender : INotificationSender { public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken = default) => Task.FromException(new IOException("simulated notification failure")); }
    private sealed class RecordingObserver : IEventObserver { public List<ApexPostEvent> Events { get; } = []; public Task OnEventAsync(ApexPostEvent applicationEvent, CancellationToken cancellationToken = default) { Events.Add(applicationEvent); return Task.CompletedTask; } }
}
