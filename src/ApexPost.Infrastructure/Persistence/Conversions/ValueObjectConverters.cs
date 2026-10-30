using ApexPost.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ApexPost.Infrastructure.Persistence.Conversions;

// Value objects are stored as their primitive value. Reading a row goes back through the value object's
// constructor, so a row that violates a domain rule fails loudly instead of producing an invalid entity.

internal sealed class BranchCodeConverter() : ValueConverter<BranchCode, string>(value => value.Value, value => new BranchCode(value));

internal sealed class CounterCodeConverter() : ValueConverter<CounterCode, string>(value => value.Value, value => new CounterCode(value));

internal sealed class StaffIdConverter() : ValueConverter<StaffId, string>(value => value.Value, value => new StaffId(value));

internal sealed class ProductIdConverter() : ValueConverter<ProductId, string>(value => value.Value, value => new ProductId(value));

internal sealed class SkuConverter() : ValueConverter<Sku, string>(value => value.Value, value => new Sku(value));

internal sealed class ShipmentIdConverter() : ValueConverter<ShipmentId, string>(value => value.Value, value => new ShipmentId(value));

internal sealed class ZoneCodeConverter() : ValueConverter<ZoneCode, string>(value => value.Value, value => new ZoneCode(value));

internal sealed class CounterTransactionIdConverter() : ValueConverter<CounterTransactionId, string>(value => value.Value, value => new CounterTransactionId(value));

internal sealed class MoneyOrderIdConverter() : ValueConverter<MoneyOrderId, string>(value => value.Value, value => new MoneyOrderId(value));

internal sealed class MoneyOrderContextIdConverter() : ValueConverter<MoneyOrderContextId, string>(value => value.Value, value => new MoneyOrderContextId(value));

internal sealed class MoneyConverter() : ValueConverter<Money, decimal>(value => value.Amount, value => new Money(value));

internal sealed class ParcelWeightConverter() : ValueConverter<ParcelWeight, decimal>(value => value.Kilograms, value => new ParcelWeight(value));

/// <summary>Stores instants as UTC datetime2; the offset is always zero, so storing it would only waste space.</summary>
internal sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTime>(
    value => value.UtcDateTime,
    value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
