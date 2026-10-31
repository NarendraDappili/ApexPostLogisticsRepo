using ApexPost.Application.Events;
using ApexPost.Application.Inventory;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Tests.Inventory;

public sealed class StockAdjustmentServiceTests
{
    [Fact]
    public async Task An_adjustment_of_existing_stock_runs_the_procedure_in_one_transaction_and_raises_a_low_stock_alert()
    {
        var fixture = new Fixture(stock: 12);

        var level = await fixture.Service.AdjustAsync(new StockAdjustmentRequest("BR-A", "P-1", -3, "Damaged in transit"), fixture.Manager.Id);

        Assert.Equal(9, level.Quantity);
        var applied = Assert.Single(fixture.Procedure.Calls);
        Assert.Equal(("SKU-P-1", -3, "Damaged in transit", "MGR-A"), (applied.Sku.Value, applied.Adjustment.Delta, applied.Adjustment.Reason, applied.Adjustment.ChangedBy.Value));
        Assert.Equal((1, 0), (fixture.UnitOfWork.Transactions, fixture.UnitOfWork.Commits));
        Assert.Contains(fixture.Events.Published, published => published is StockLow { Quantity: 9 });
    }

    [Fact]
    public async Task A_first_delivery_saves_a_new_stock_row_before_the_procedure_inside_the_same_transaction()
    {
        var fixture = new Fixture(stock: null);

        await fixture.Service.AdjustAsync(new StockAdjustmentRequest("BR-A", "P-1", 20, "First delivery"), fixture.Manager.Id);

        Assert.Equal(0, Assert.Single(fixture.Inventory.Added).Quantity);
        Assert.Equal((1, 1), (fixture.UnitOfWork.Transactions, fixture.UnitOfWork.Commits));
        Assert.Single(fixture.Procedure.Calls);
    }

    [Fact]
    public async Task Removing_stock_the_branch_does_not_hold_is_refused_without_touching_the_database()
    {
        var fixture = new Fixture(stock: null);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AdjustAsync(new StockAdjustmentRequest("BR-A", "P-1", -1, "Count"), fixture.Manager.Id));

        Assert.Equal("The branch does not stock this product.", refused.Message);
        Assert.Empty(fixture.Procedure.Calls);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData("CLERK-A")]
    [InlineData("MGR-B")]
    public async Task Only_a_branch_manager_of_the_same_branch_may_adjust_stock(string staffId)
    {
        var fixture = new Fixture(stock: 5);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AdjustAsync(new StockAdjustmentRequest("BR-A", "P-1", 1, "Count"), new StaffId(staffId)));

        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task A_zero_change_or_a_missing_reason_is_rejected_by_the_domain_rules_first()
    {
        var fixture = new Fixture(stock: 5);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.AdjustAsync(new StockAdjustmentRequest("BR-A", "P-1", 0, "Count"), fixture.Manager.Id));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => fixture.Service.AdjustAsync(new StockAdjustmentRequest("BR-A", "P-1", 1, " "), fixture.Manager.Id));

        Assert.Empty(fixture.Procedure.Calls);
    }

    private sealed class Fixture
    {
        public Fixture(int? stock)
        {
            Inventory = new InventoryRepo(stock is { } quantity ? new BranchInventoryItem(new("BR-A"), new("P-1"), quantity) : null);
            Procedure = new RecordingProcedure(stock ?? 0);
            var events = new EventPublisher([Events]);
            Service = new StockAdjustmentService(
                new StaffRepo([Manager, new(new("MGR-B"), "Other Manager", StaffRole.BranchManager, new("BR-B")), new(new("CLERK-A"), "Clerk", StaffRole.CounterClerk, new("BR-A"))]),
                new ProductRepo(new Product(new("P-1"), "Envelope", new Money(1m), new Sku("SKU-P-1"))),
                Inventory, Procedure, UnitOfWork,
                new InventoryService(Inventory, UnitOfWork, new InventoryServiceOptions { LowStockThreshold = 10 }, events));
        }

        public StaffMember Manager { get; } = new(new("MGR-A"), "Manager", StaffRole.BranchManager, new("BR-A"));
        public InventoryRepo Inventory { get; }
        public RecordingProcedure Procedure { get; }
        public SpyUnitOfWork UnitOfWork { get; } = new();
        public RecordingObserver Events { get; } = new();
        public StockAdjustmentService Service { get; }
    }

    private sealed class RecordingProcedure(int quantity) : IStockAdjustments
    {
        private int _quantity = quantity;
        public List<(StockAdjustment Adjustment, Sku Sku)> Calls { get; } = [];

        public Task<int> ApplyAsync(StockAdjustment adjustment, Sku sku, CancellationToken cancellationToken = default)
        {
            Calls.Add((adjustment, sku));
            _quantity += adjustment.Delta;
            return Task.FromResult(_quantity);
        }
    }

    private sealed class InventoryRepo(BranchInventoryItem? item) : IInventoryRepository
    {
        public List<BranchInventoryItem> Added { get; } = [];
        public Task<BranchInventoryItem?> FindAsync(BranchCode branchCode, ProductId productId, CancellationToken cancellationToken = default) => Task.FromResult(item);
        public void Add(BranchInventoryItem inventoryItem) => Added.Add(inventoryItem);
        public void Update(BranchInventoryItem inventoryItem) { }
    }

    private sealed class StaffRepo(IReadOnlyCollection<StaffMember> staff) : IStaffMemberRepository
    {
        public Task<StaffMember?> FindByIdAsync(StaffId id, CancellationToken cancellationToken = default) => Task.FromResult(staff.FirstOrDefault(member => member.Id == id));
        public void Add(StaffMember staffMember) { }
        public void Update(StaffMember staffMember) { }
    }

    private sealed class ProductRepo(Product product) : IProductRepository
    {
        public Task<Product?> FindByIdAsync(ProductId id, CancellationToken cancellationToken = default) => Task.FromResult<Product?>(id == product.Id ? product : null);
    }

    private sealed class RecordingObserver : IEventObserver
    {
        public List<ApexPostEvent> Published { get; } = [];
        public Task OnEventAsync(ApexPostEvent applicationEvent, CancellationToken cancellationToken = default) { Published.Add(applicationEvent); return Task.CompletedTask; }
    }
}
