using System.Runtime.CompilerServices;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using ApexPost.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Repositories;

// EF Core repositories for the command side: they load one aggregate a use case may change, and stage changes
// (Add/Update) in the request's scoped ApexPostDbContext without calling SaveChanges; EfUnitOfWork commits.
// Screens and reports read through the query services instead (Infrastructure/Queries).

public sealed class EfBranchRepository(ApexPostDbContext db) : IBranchRepository
{
    public Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default) =>
        db.Branches.SingleOrDefaultAsync(branch => branch.Code == code, cancellationToken);

    public void Add(Branch branch) => db.Branches.Add(branch);

    public void Update(Branch branch) => Staging.EnsureTracked(db, branch);
}

public sealed class EfCounterRepository(ApexPostDbContext db) : ICounterRepository
{
    public Task<Counter?> FindByCodeAsync(BranchCode branchCode, CounterCode counterCode, CancellationToken cancellationToken = default) =>
        db.Counters.SingleOrDefaultAsync(counter => counter.BranchCode == branchCode && counter.Code == counterCode, cancellationToken);

    public void Add(Counter counter) => db.Counters.Add(counter);
}

public sealed class EfStaffMemberRepository(ApexPostDbContext db) : IStaffMemberRepository
{
    public Task<StaffMember?> FindByIdAsync(StaffId id, CancellationToken cancellationToken = default) =>
        db.Staff.SingleOrDefaultAsync(staff => staff.Id == id, cancellationToken);

    public void Add(StaffMember staffMember) => db.Staff.Add(staffMember);

    public void Update(StaffMember staffMember) => Staging.EnsureTracked(db, staffMember);
}

public sealed class EfProductRepository(ApexPostDbContext db) : IProductRepository
{
    // Use cases only read a product's price; prices are revised set-based, so nothing is tracked.
    public Task<Product?> FindByIdAsync(ProductId id, CancellationToken cancellationToken = default) =>
        db.Products.AsNoTracking().SingleOrDefaultAsync(product => product.Id == id, cancellationToken);
}

public sealed class EfInventoryRepository(ApexPostDbContext db) : IInventoryRepository
{
    // Tracked: the rowversion read here is the one SaveChanges checks, which is what stops two clerks
    // from both selling the last unit.
    public Task<BranchInventoryItem?> FindAsync(BranchCode branchCode, ProductId productId, CancellationToken cancellationToken = default) =>
        db.BranchStock.SingleOrDefaultAsync(item => item.BranchCode == branchCode && item.ProductId == productId, cancellationToken);

    public void Add(BranchInventoryItem inventoryItem) => db.BranchStock.Add(inventoryItem);

    public void Update(BranchInventoryItem inventoryItem) => Staging.EnsureTracked(db, inventoryItem);
}

public sealed class EfShipmentRepository(ApexPostDbContext db) : IShipmentRepository
{
    // A shipment never changes after creation, so reads are not tracked.
    public Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken cancellationToken = default) =>
        db.Shipments.AsNoTracking().SingleOrDefaultAsync(shipment => shipment.Id == id, cancellationToken);

    public void Add(Shipment shipment) => db.Shipments.Add(shipment);
}

/// <summary>
/// Money orders are stored through separate persistence entities (see <see cref="MoneyOrderMapper"/>).
/// The repository remembers which tracked entity backs each domain object it handed out, so
/// <see cref="Update"/> can copy the domain change onto that entity.
/// </summary>
public sealed class EfMoneyOrderRepository(ApexPostDbContext db) : IMoneyOrderRepository
{
    private readonly ConditionalWeakTable<MoneyOrder, MoneyOrderEntity> _loaded = new();

    public async Task<MoneyOrder?> FindByIdAsync(MoneyOrderId id, CancellationToken cancellationToken = default)
    {
        // Include: the verification is part of the aggregate the use case may change, so it is tracked with it.
        var entity = await db.MoneyOrders.Include(order => order.IdentityVerification)
            .SingleOrDefaultAsync(order => order.MoneyOrderCode == id, cancellationToken).ConfigureAwait(false);
        if (entity is null) return null;

        var order = MoneyOrderMapper.ToDomain(entity);
        _loaded.AddOrUpdate(order, entity);
        return order;
    }

    public void Add(MoneyOrder moneyOrder)
    {
        var entity = MoneyOrderMapper.ToEntity(moneyOrder);
        db.MoneyOrders.Add(entity);
        _loaded.AddOrUpdate(moneyOrder, entity);
    }

    public void Update(MoneyOrder moneyOrder)
    {
        ArgumentNullException.ThrowIfNull(moneyOrder);
        if (!_loaded.TryGetValue(moneyOrder, out var entity))
        {
            throw new InvalidOperationException("Load a money order through this repository before updating it.");
        }

        MoneyOrderMapper.Apply(moneyOrder, entity);
    }
}

public sealed class EfCounterTransactionRepository(ApexPostDbContext db) : ICounterTransactionRepository
{
    // Lines and payment are owned, so EF loads them with the transaction; no Include is needed.
    public Task<CounterTransaction?> FindByIdAsync(CounterTransactionId id, CancellationToken cancellationToken = default) =>
        db.CounterTransactions.SingleOrDefaultAsync(transaction => transaction.Id == id, cancellationToken);

    public void Add(CounterTransaction transaction) => db.CounterTransactions.Add(transaction);

    public void Update(CounterTransaction transaction) => Staging.EnsureTracked(db, transaction);
}

internal static class Staging
{
    /// <summary>
    /// A loaded aggregate is already tracked, so its changes are picked up by SaveChanges. Attaching a detached copy
    /// is refused: its shadow key and rowversion are unknown, so EF would insert it again or skip the concurrency check.
    /// </summary>
    public static void EnsureTracked<TEntity>(ApexPostDbContext db, TEntity entity)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (db.Entry(entity).State == EntityState.Detached)
        {
            throw new InvalidOperationException($"Load the {typeof(TEntity).Name} through its repository before updating it.");
        }
    }
}
