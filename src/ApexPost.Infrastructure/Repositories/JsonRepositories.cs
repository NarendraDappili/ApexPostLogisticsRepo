using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;

namespace ApexPost.Infrastructure.Repositories;

public sealed class JsonBranchRepository(JsonRepositoryOptions options) : IBranchRepository
{
    private readonly JsonFileStore<List<BranchDto>> _store = new(options, "branches.json");
    public async Task<Branch?> FindByCodeAsync(BranchCode code, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Code == code.Value)?.ToEntity();
    public Task SaveAsync(Branch branch, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, branch, static x => x.Code, BranchDto.From, cancellationToken);
}

public sealed class JsonCounterRepository(JsonRepositoryOptions options) : ICounterRepository
{
    private readonly JsonFileStore<List<CounterDto>> _store = new(options, "counters.json");
    public async Task<Counter?> FindByCodeAsync(BranchCode branchCode, CounterCode counterCode, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.BranchCode == branchCode.Value && x.Code == counterCode.Value)?.ToEntity();
    public Task SaveAsync(Counter counter, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, counter, static x => $"{x.BranchCode}\u001f{x.Code}", CounterDto.From, cancellationToken);
}

public sealed class JsonStaffMemberRepository(JsonRepositoryOptions options) : IStaffMemberRepository
{
    private readonly JsonFileStore<List<StaffDto>> _store = new(options, "staff-members.json");
    public async Task<StaffMember?> FindByIdAsync(StaffId id, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id.Value)?.ToEntity();
    public Task SaveAsync(StaffMember staffMember, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, staffMember, static x => x.Id, StaffDto.From, cancellationToken);
}

public sealed class JsonProductRepository(JsonRepositoryOptions options) : IProductRepository
{
    private readonly JsonFileStore<List<ProductDto>> _store = new(options, "products.json");
    public async Task<Product?> FindByIdAsync(ProductId id, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id.Value)?.ToEntity();
    public Task SaveAsync(Product product, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, product, static x => x.Id, ProductDto.From, cancellationToken);
}

public sealed class JsonInventoryRepository(JsonRepositoryOptions options) : IInventoryRepository
{
    private readonly JsonFileStore<List<InventoryDto>> _store = new(options, "inventory.json");
    public async Task<BranchInventoryItem?> FindAsync(BranchCode branchCode, ProductId productId, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.BranchCode == branchCode.Value && x.ProductId == productId.Value)?.ToEntity();
    public Task SaveAsync(BranchInventoryItem inventoryItem, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, inventoryItem, static x => $"{x.BranchCode}\u001f{x.ProductId}", InventoryDto.From, cancellationToken);
}

public sealed class JsonShipmentRepository(JsonRepositoryOptions options) : IShipmentRepository
{
    private readonly JsonFileStore<List<ShipmentDto>> _store = new(options, "shipments.json");
    public async Task<Shipment?> FindByIdAsync(ShipmentId id, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id.Value)?.ToEntity();
    public Task SaveAsync(Shipment shipment, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, shipment, static x => x.Id, ShipmentDto.From, cancellationToken);
}

public sealed class JsonMoneyOrderRepository(JsonRepositoryOptions options) : IMoneyOrderRepository
{
    private readonly JsonFileStore<List<MoneyOrderDto>> _store = new(options, "money-orders.json");
    public async Task<MoneyOrder?> FindByIdAsync(MoneyOrderId id, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(static () => [], cancellationToken).ConfigureAwait(false)).FirstOrDefault(x => x.Id == id.Value)?.ToEntity();
    public Task SaveAsync(MoneyOrder moneyOrder, CancellationToken cancellationToken = default) => JsonRepositorySave.SaveAsync(_store, moneyOrder, static x => x.Id, MoneyOrderDto.From, cancellationToken);
}

internal static class JsonRepositorySave
{
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

internal sealed record BranchDto(string Code, string Name, bool IsOpen)
{ public static BranchDto From(Branch x) => new(x.Code.Value, x.Name, x.IsOpen); public Branch ToEntity() { var x = new Branch(new(Code), Name); if (!IsOpen) x.Close(); return x; } }
internal sealed record CounterDto(string Code, string BranchCode, bool IsOpen)
{ public static CounterDto From(Counter x) => new(x.Code.Value, x.BranchCode.Value, x.IsOpen); public Counter ToEntity() { var x = new Counter(new(Code), new(BranchCode)); if (!IsOpen) x.Close(); return x; } }
internal sealed record StaffDto(string Id, string Name, StaffRole Role, string BranchCode, bool IsActive)
{ public static StaffDto From(StaffMember x) => new(x.Id.Value, x.Name, x.Role, x.AssignedBranchCode.Value, x.IsActive); public StaffMember ToEntity() { var x = new StaffMember(new(Id), Name, Role, new(BranchCode)); if (!IsActive) x.Deactivate(); return x; } }
internal sealed record ProductDto(string Id, string Name, decimal UnitPrice)
{ public static ProductDto From(Product x) => new(x.Id.Value, x.Name, x.UnitPrice.Amount); public Product ToEntity() => new(new ProductId(Id), Name, new(UnitPrice)); }
internal sealed record InventoryDto(string BranchCode, string ProductId, int Quantity)
{ public static InventoryDto From(BranchInventoryItem x) => new(x.BranchCode.Value, x.ProductId.Value, x.Quantity); public BranchInventoryItem ToEntity() => new(new(BranchCode), new(ProductId), Quantity); }
internal sealed record ShipmentDto(string Id, string OriginBranchCode, string DestinationZoneCode, decimal WeightKilograms, decimal LengthCentimetres, decimal WidthCentimetres, decimal HeightCentimetres)
{ public static ShipmentDto From(Shipment x) => new(x.Id.Value, x.OriginBranchCode.Value, x.DestinationZoneCode.Value, x.Weight.Kilograms, x.Dimensions.LengthCentimetres, x.Dimensions.WidthCentimetres, x.Dimensions.HeightCentimetres); public Shipment ToEntity() => new(new(Id), new(OriginBranchCode), new(DestinationZoneCode), new(WeightKilograms), new(LengthCentimetres, WidthCentimetres, HeightCentimetres)); }
internal sealed record MoneyOrderDto(string Id, string BranchCode, decimal Amount, IdentityVerificationOutcome VerificationOutcome)
{ public static MoneyOrderDto From(MoneyOrder x) => new(x.Id.Value, x.BranchCode.Value, x.Amount.Amount, x.IdentityVerificationOutcome); public MoneyOrder ToEntity() { var x = new MoneyOrder(new(Id), new(BranchCode), new(Amount)); if (VerificationOutcome == IdentityVerificationOutcome.Verified) x.MarkIdentityVerified(); else if (VerificationOutcome == IdentityVerificationOutcome.Failed) x.MarkIdentityVerificationFailed(); return x; } }
