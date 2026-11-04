using System.Data;
using System.Globalization;
using ApexPost.Application.Auditing;
using ApexPost.Application.Catalogue;
using ApexPost.Application.Inventory;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using ApexPost.Infrastructure.Persistence.StoredProcedures;
using ApexPost.Infrastructure.Queries;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Auditing;

/// <summary>
/// Adds the DataChanges history to the set-based price revision. <see cref="EfCatalogueBulkUpdates"/> stays one
/// UPDATE statement; around it, in one transaction, this reads the old prices (holding update locks, so the prices
/// read are exactly the ones the UPDATE replaces), then the new ones, and saves one DataChanges row per product.
/// The revision and its history commit together or not at all.
/// </summary>
public sealed class AuditedCatalogueBulkUpdates(EfCatalogueBulkUpdates inner, ApexPostDbContext db) : ICatalogueBulkUpdates
{
    public Task<int> RevisePricesAsync(string categoryCode, decimal factor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryCode);
        if (db.Database.CurrentTransaction is not null) return ReviseAndRecordAsync(categoryCode, factor, cancellationToken);

        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var repriced = await ReviseAndRecordAsync(categoryCode, factor, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return repriced;
        });
    }

    private async Task<int> ReviseAndRecordAsync(string categoryCode, decimal factor, CancellationToken cancellationToken)
    {
        var before = await PricesAsync(categoryCode, holdUpdateLocks: true, cancellationToken).ConfigureAwait(false);
        var repriced = await inner.RevisePricesAsync(categoryCode, factor, cancellationToken).ConfigureAwait(false);
        if (before.Count == 0) return repriced;

        var after = (await PricesAsync(categoryCode, holdUpdateLocks: false, cancellationToken).ConfigureAwait(false)).ToDictionary(row => row.ProductCode, row => row.UnitPrice, StringComparer.Ordinal);
        foreach (var row in before)
        {
            if (!after.TryGetValue(row.ProductCode, out var newPrice) || newPrice == row.UnitPrice) continue;
            db.DataChanges.Add(new DataChangeEntity
            {
                TableName = "Products",
                RecordKey = $"ProductCode={row.ProductCode}",
                Operation = nameof(DataChangeOperation.Update),
                ChangesJson = DataChangeJson.Serialize([new DataChangeColumn("UnitPrice", Text(row.UnitPrice), Text(newPrice))]),
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return repriced;
    }

    // The category is a varchar parameter, matching its column, so the comparison needs no conversion.
    private Task<List<PriceRow>> PricesAsync(string categoryCode, bool holdUpdateLocks, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<PriceRow>(
                holdUpdateLocks ? LockedPricesSql : PricesSql,
                new SqlParameter("@CategoryCode", SqlDbType.VarChar, ProductCategory.MaxCodeLength) { Value = categoryCode })
            .ToListAsync(cancellationToken);

    private const string PricesSql = "SELECT ProductCode, UnitPrice FROM dbo.Products WHERE CategoryCode = @CategoryCode";
    private const string LockedPricesSql = "SELECT ProductCode, UnitPrice FROM dbo.Products WITH (UPDLOCK, HOLDLOCK) WHERE CategoryCode = @CategoryCode";

    private static string Text(decimal price) => price.ToString("0.00", CultureInfo.InvariantCulture);

    private sealed class PriceRow
    {
        public string ProductCode { get; init; } = string.Empty;

        public decimal UnitPrice { get; init; }
    }
}

/// <summary>
/// Adds the DataChanges history to a stock correction. <see cref="EfStockAdjustments"/> runs usp_AdjustStock inside
/// the caller's transaction; this then saves, in that same transaction, the stock quantity's old and new value and the
/// StockAdjustments row the procedure wrote.
/// </summary>
public sealed class AuditedStockAdjustments(EfStockAdjustments inner, ApexPostDbContext db) : IStockAdjustments
{
    public async Task<int> ApplyAsync(StockAdjustment adjustment, Sku sku, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        var newQuantity = await inner.ApplyAsync(adjustment, sku, cancellationToken).ConfigureAwait(false);
        var key = $"BranchCode={adjustment.BranchCode.Value}, ProductCode={adjustment.ProductId.Value}";
        db.DataChanges.AddRange(
            Row("BranchStock", key, DataChangeOperation.Update, adjustment,
                [new DataChangeColumn("Quantity", Text(newQuantity - adjustment.Delta), Text(newQuantity))]),
            Row("StockAdjustments", key, DataChangeOperation.Insert, adjustment,
                [new DataChangeColumn("Delta", null, Text(adjustment.Delta)), new DataChangeColumn("Reason", null, adjustment.Reason), new DataChangeColumn("ChangedByStaffCode", null, adjustment.ChangedBy.Value)]));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return newQuantity;
    }

    private static DataChangeEntity Row(string table, string key, DataChangeOperation operation, StockAdjustment adjustment, DataChangeColumn[] columns) => new()
    {
        OccurredAtUtc = adjustment.OccurredAtUtc,
        StaffId = adjustment.ChangedBy.Value,
        BranchCode = adjustment.BranchCode.Value,
        TableName = table,
        RecordKey = key,
        Operation = operation.ToString(),
        ChangesJson = DataChangeJson.Serialize(columns),
    };

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
