using System.Runtime.CompilerServices;
using ApexPost.Application.Abstractions;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;

namespace ApexPost.Infrastructure.Repositories;

public sealed class JsonBranchRepository(JsonRepositoryOptions options, JsonUnitOfWork? unitOfWork = null) : IBranchRepository
{
    private readonly JsonFileStore<List<BranchDto>> _store = new(options, "branches.json");
    public async Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Code == code.Value)?.ToEntity();
    public async Task<IReadOnlyCollection<Branch>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).Select(item => item.ToEntity()).ToArray();
    public Task SaveAsync(Branch branch, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, branch, static x => x.Code, BranchDto.From, cancellationToken);
    public void Add(Branch branch) => JsonRepositorySave.Stage(unitOfWork, token => SaveAsync(branch, token));
    public void Update(Branch branch) => JsonRepositorySave.Stage(unitOfWork, token => SaveAsync(branch, token));
}

public sealed class JsonCounterRepository(JsonRepositoryOptions options, JsonUnitOfWork? unitOfWork = null) : ICounterRepository
{
    private readonly JsonFileStore<List<CounterDto>> _store = new(options, "counters.json");
    public async Task<Counter?> FindByCodeAsync(BranchCode branchCode, CounterCode counterCode, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.BranchCode == branchCode.Value && x.Code == counterCode.Value)?.ToEntity();
    public async Task<IReadOnlyCollection<Counter>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).Where(x => x.BranchCode == branchCode.Value).Select(x => x.ToEntity()).ToArray();
    public Task SaveAsync(Counter counter, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, counter, static x => $"{x.BranchCode}\u001f{x.Code}", CounterDto.From, cancellationToken);
    public void Add(Counter counter) => JsonRepositorySave.Stage(unitOfWork, token => SaveAsync(counter, token));
}

public sealed class JsonStaffMemberRepository(JsonRepositoryOptions options, JsonUnitOfWork? unitOfWork = null) : IStaffMemberRepository
{
    private readonly JsonFileStore<List<StaffDto>> _store = new(options, "staff-members.json");
    public async Task<StaffMember?> FindByIdAsync(StaffId id, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id.Value)?.ToEntity();
    public async Task<IReadOnlyCollection<StaffMember>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).Select(item => item.ToEntity()).ToArray();
    public Task SaveAsync(StaffMember staffMember, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, staffMember, static x => x.Id, StaffDto.From, cancellationToken);
    public void Add(StaffMember staffMember) => JsonRepositorySave.Stage(unitOfWork, token => SaveAsync(staffMember, token));
    public void Update(StaffMember staffMember) => JsonRepositorySave.Stage(unitOfWork, token => SaveAsync(staffMember, token));
}

public sealed class JsonProductRepository(JsonRepositoryOptions options) : IProductRepository
{
    private readonly JsonFileStore<List<ProductDto>> _store = new(options, "products.json");
    public async Task<Product?> FindByIdAsync(ProductId id, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id.Value)?.ToEntity();
    public async Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).Select(item => item.ToEntity()).OrderBy(item => item.Id.Value).ToArray();
    public Task SaveAsync(Product product, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, product, static x => x.Id, ProductDto.From, cancellationToken);
}

public sealed class JsonInventoryRepository(JsonRepositoryOptions options, JsonUnitOfWork? unitOfWork = null) : IInventoryRepository
{
    private readonly JsonFileStore<List<InventoryDto>> _store = new(options, "inventory.json");
    // The quantity each item had when this unit of work read it: the JSON stand-in for a rowversion.
    private readonly ConditionalWeakTable<BranchInventoryItem, StrongBox<int>> _readQuantities = new();
    // Identity map, as a change tracker keeps: a basket with two lines for one product changes one object.
    private readonly Dictionary<(string Branch, string Product), BranchInventoryItem> _loaded = [];
    private readonly HashSet<BranchInventoryItem> _staged = new(ReferenceEqualityComparer.Instance);

    public async Task<BranchInventoryItem?> FindAsync(BranchCode branchCode, ProductId productId, CancellationToken cancellationToken = default)
    {
        if (_loaded.TryGetValue((branchCode.Value, productId.Value), out var loaded)) return loaded;
        return Track((await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.BranchCode == branchCode.Value && x.ProductId == productId.Value)?.ToEntity());
    }
    public Task SaveAsync(BranchInventoryItem inventoryItem, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, inventoryItem, static x => $"{x.BranchCode}\u001f{x.ProductId}", InventoryDto.From, cancellationToken);
    public async Task<IReadOnlyCollection<BranchInventoryItem>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).Where(item => item.BranchCode == branchCode.Value).Select(item => Track(item.ToEntity())!).ToArray();

    /// <summary>Stages a write that succeeds only if the stored quantity is still the one this unit of work read.</summary>
    public void Update(BranchInventoryItem inventoryItem)
    {
        ArgumentNullException.ThrowIfNull(inventoryItem);
        if (!_readQuantities.TryGetValue(inventoryItem, out var readQuantity))
        {
            throw new InvalidOperationException("Only stock read through this repository can be updated.");
        }

        // The write copies the item's state at commit time, so one staged write covers every later change.
        if (!_staged.Add(inventoryItem)) return;
        JsonRepositorySave.Stage(unitOfWork, token => _store.UpdateAsync(static () => [], items =>
        {
            var index = items.FindIndex(item => item.BranchCode == inventoryItem.BranchCode.Value && item.ProductId == inventoryItem.ProductId.Value);
            if (index < 0) throw new InvalidOperationException("No inventory item exists for the branch and product.");
            if (items[index].Quantity != readQuantity.Value)
            {
                throw new ConcurrencyConflictException("The stock level changed while this request was being processed. Check the current stock and try again.");
            }

            items[index] = InventoryDto.From(inventoryItem);
            readQuantity.Value = inventoryItem.Quantity;
            _staged.Remove(inventoryItem);
        }, token));
    }

    private BranchInventoryItem? Track(BranchInventoryItem? item)
    {
        if (item is null) return null;
        if (_loaded.TryGetValue((item.BranchCode.Value, item.ProductId.Value), out var loaded)) return loaded;
        _loaded[(item.BranchCode.Value, item.ProductId.Value)] = item;
        _readQuantities.AddOrUpdate(item, new StrongBox<int>(item.Quantity));
        return item;
    }
}

public sealed class JsonShipmentRepository(JsonRepositoryOptions options, JsonUnitOfWork? unitOfWork = null) : IShipmentRepository
{
    private readonly JsonFileStore<List<ShipmentDto>> _store = new(options, "shipments.json");
    public async Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id.Value)?.ToEntity();
    public Task SaveAsync(Shipment shipment, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, shipment, static x => x.Id, ShipmentDto.From, cancellationToken);
    public void Add(Shipment shipment) => JsonRepositorySave.Stage(unitOfWork, token => SaveAsync(shipment, token));
    public async Task<IReadOnlyCollection<Shipment>> FindByOriginBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).Where(item => item.OriginBranchCode == branchCode.Value).Select(item => item.ToEntity()).ToArray();
}

public sealed class JsonMoneyOrderRepository(JsonRepositoryOptions options, JsonUnitOfWork? unitOfWork = null) : IMoneyOrderRepository
{
    private readonly JsonFileStore<List<MoneyOrderDto>> _store = new(options, "money-orders.json");
    public async Task<MoneyOrder?> FindByIdAsync(MoneyOrderId id, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id.Value)?.ToEntity();
    public Task SaveAsync(MoneyOrder moneyOrder, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, moneyOrder, static x => x.Id, MoneyOrderDto.From, cancellationToken);
    public void Add(MoneyOrder moneyOrder) => JsonRepositorySave.Stage(unitOfWork, token => SaveAsync(moneyOrder, token));
    public void Update(MoneyOrder moneyOrder) => JsonRepositorySave.Stage(unitOfWork, token => SaveAsync(moneyOrder, token));
    public async Task<IReadOnlyCollection<MoneyOrder>> FindByBranchAsync(BranchCode branchCode, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).Where(item => item.BranchCode == branchCode.Value).Select(item => item.ToEntity()).ToArray();
}

internal static class JsonRepositorySave
{
    // SaveAsync on each JSON repository writes immediately and is kept for the JSON seeder; the interface
    // methods Add and Update only queue that write in the unit of work.
    public static void Stage(JsonUnitOfWork? unitOfWork, Func<CancellationToken, Task> write) =>
        (unitOfWork ?? throw new InvalidOperationException("This repository was created without a unit of work, so it cannot stage changes.")).Stage(write);

    public static Task SaveAsync<TEntity, TDto>(JsonFileStore<List<TDto>> store, TEntity entity, Func<TDto, string> key, Func<TEntity, TDto> map, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return store.UpdateAsync(static () => [], items =>
        {
            var dto = map(entity);
            var index = items.FindIndex(existing => key(existing) == key(dto));
            if (index < 0) items.Add(dto); else items[index] = dto;
        }, cancellationToken);
    }
}

internal sealed record BranchDto(string Code, string Name, bool IsOpen, string? Region = null, string? OpeningHours = null)
{ public static BranchDto From(Branch x) => new(x.Code.Value, x.Name, x.IsOpen, x.Region, x.OpeningHours); public Branch ToEntity() { var x = new Branch(new(Code), Name, Region ?? Branch.UnassignedRegion, OpeningHours ?? Branch.UnconfiguredOpeningHours); if (!IsOpen) x.Close(); return x; } }
internal sealed record CounterDto(string Code, string BranchCode, bool IsOpen)
{ public static CounterDto From(Counter x) => new(x.Code.Value, x.BranchCode.Value, x.IsOpen); public Counter ToEntity() { var x = new Counter(new(Code), new(BranchCode)); if (!IsOpen) x.Close(); return x; } }
internal sealed record StaffDto(string Id, string Name, StaffRole Role, string BranchCode, bool IsActive, string? Email = null, string? PasswordHash = null)
{
    public static StaffDto From(StaffMember x) => new(x.Id.Value, x.Name, x.Role, x.AssignedBranchCode.Value, x.IsActive, x.Email, x.PasswordHash);
    public StaffMember ToEntity()
    {
        var x = new StaffMember(new(Id), Name, Role, new(BranchCode), Email);
        if (PasswordHash is not null) x.SetPasswordHash(PasswordHash);
        if (!IsActive) x.Deactivate();
        return x;
    }
}
internal sealed record ProductDto(string Id, string Name, decimal UnitPrice)
{ public static ProductDto From(Product x) => new(x.Id.Value, x.Name, x.UnitPrice.Amount); public Product ToEntity() => new(new ProductId(Id), Name, new(UnitPrice)); }
internal sealed record InventoryDto(string BranchCode, string ProductId, int Quantity)
{ public static InventoryDto From(BranchInventoryItem x) => new(x.BranchCode.Value, x.ProductId.Value, x.Quantity); public BranchInventoryItem ToEntity() => new(new(BranchCode), new(ProductId), Quantity); }
internal sealed record ShipmentDto(string Id, string OriginBranchCode, string DestinationZoneCode, decimal WeightKilograms, decimal LengthCentimetres, decimal WidthCentimetres, decimal HeightCentimetres, ShipmentType ShipmentType, ShippingServiceLevel ServiceLevel, string? TransactionId = null)
{ public static ShipmentDto From(Shipment x) => new(x.Id.Value, x.OriginBranchCode.Value, x.DestinationZoneCode.Value, x.Weight.Kilograms, x.Dimensions.LengthCentimetres, x.Dimensions.WidthCentimetres, x.Dimensions.HeightCentimetres, x.ShipmentType, x.ServiceLevel, x.TransactionId?.Value); public Shipment ToEntity() => new(new(Id), new(OriginBranchCode), new(DestinationZoneCode), new(WeightKilograms), new(LengthCentimetres, WidthCentimetres, HeightCentimetres), ShipmentType, ServiceLevel, TransactionId is null ? null : new CounterTransactionId(TransactionId)); }
internal sealed record MoneyOrderDto(string Id, string BranchCode, decimal Amount, string Sender, string Recipient, decimal Fee, string TransactionContextId, IdentityVerificationOutcome VerificationOutcome, string? VerificationDocumentType, string? MaskedDocumentReference)
{
    public static MoneyOrderDto From(MoneyOrder x) => new(x.Id.Value, x.BranchCode.Value, x.Amount.Amount, x.Sender, x.Recipient, x.Fee.Amount, x.TransactionContextId.Value, x.IdentityVerificationOutcome, x.IdentityVerification?.DocumentType, x.IdentityVerification?.MaskedDocumentReference.Value);
    public MoneyOrder ToEntity()
    {
        var x = new MoneyOrder(new(Id), new(BranchCode), new(Amount), Sender, Recipient, new(Fee), new(TransactionContextId));
        if (VerificationDocumentType is not null && MaskedDocumentReference is not null)
        {
            x.RecordIdentityVerification(new(new(TransactionContextId), VerificationDocumentType, MaskedIdentityDocumentReference.FromMaskedValue(MaskedDocumentReference), VerificationOutcome));
        }
        return x;
    }
}
