using ApexPost.Application.Abstractions;
using ApexPost.Application.Inventory;
using ApexPost.Application.MoneyOrders;
using ApexPost.Application.Pricing;
using ApexPost.Application.Transactions;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using ApexPost.Infrastructure.Pricing;
using ApexPost.Infrastructure.Repositories;

namespace ApexPost.Infrastructure.Tests.Sql;

/// <summary>
/// One branch with a counter, a clerk, a manager, a product with stock and a shipping rate. Codes carry a unique
/// suffix so tests sharing one database never see each other's rows.
/// </summary>
internal sealed class SqlTestData
{
    private SqlTestData(string suffix)
    {
        Branch = new BranchCode($"BR-{suffix}");
        Counter = new CounterCode("C-01");
        Clerk = new StaffId($"CLK-{suffix}");
        Manager = new StaffId($"MGR-{suffix}");
        Product = new ProductId($"PRD-{suffix}");
        Zone = new ZoneCode($"Z-{suffix}");
    }

    public BranchCode Branch { get; }
    public CounterCode Counter { get; }
    public StaffId Clerk { get; }
    public StaffId Manager { get; }
    public ProductId Product { get; }
    public ZoneCode Zone { get; }

    public static async Task<SqlTestData> SeedAsync(SqlServerTestDatabase database, int stock, decimal unitPrice = 10m)
    {
        var data = new SqlTestData(Guid.NewGuid().ToString("N")[..10].ToUpperInvariant());
        await using var db = database.CreateContext();
        db.Branches.Add(new Branch(data.Branch, "Test Branch", "Test Region", "Mon-Fri"));
        db.Counters.Add(new Counter(data.Counter, data.Branch));
        db.Staff.Add(new StaffMember(data.Clerk, "Test Clerk", StaffRole.CounterClerk, data.Branch));
        db.Staff.Add(new StaffMember(data.Manager, "Test Manager", StaffRole.BranchManager, data.Branch));
        db.Products.Add(new Product(data.Product, "Test Envelope", new Money(unitPrice)));
        db.BranchStock.Add(new BranchInventoryItem(data.Branch, data.Product, stock));
        db.ShippingZones.Add(new ShippingZoneEntity { ZoneCode = data.Zone, Name = "Test Zone" });
        db.ShippingRates.Add(ShippingRateEntity.From(new ShippingRate(ShipmentType.Parcel, ShippingServiceLevel.Standard, data.Zone, new Money(8m), new ParcelWeight(5m))));
        await db.SaveChangesAsync();
        return data;
    }

    public CounterCheckoutRequest Sale(string transactionId, int quantity) =>
        new(transactionId, Branch.Value, Counter.Value, Clerk.Value, PaymentMethod.Card, [new RetailBasketLineRequest(Product.Value, quantity)], [], []);

    /// <summary>The production checkout service over EF repositories, as the web app composes it for one request.</summary>
    public static CounterTransactionWorkflowService CheckoutService(ApexPostDbContext db, decimal taxRate = 0m)
    {
        IUnitOfWork unitOfWork = new EfUnitOfWork(db);
        return new CounterTransactionWorkflowService(
            new EfBranchRepository(db), new EfCounterRepository(db), new EfStaffMemberRepository(db), new EfProductRepository(db),
            new InventoryService(new EfInventoryRepository(db), unitOfWork, new InventoryServiceOptions { LowStockThreshold = 2 }),
            new EfShipmentRepository(db), new EfShippingRateProvider(db, new ShippingRateCache()), new EfMoneyOrderRepository(db),
            new EfCounterTransactionRepository(db), new MoneyOrderPolicy(new Money(1000m)), new FixedRateTaxPolicy(taxRate), unitOfWork);
    }
}
