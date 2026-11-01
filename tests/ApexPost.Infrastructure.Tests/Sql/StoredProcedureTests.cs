using System.Data.Common;
using System.Text.RegularExpressions;
using ApexPost.Application.Abstractions;
using ApexPost.Application.Inventory;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.StoredProcedures;
using ApexPost.Infrastructure.Queries;
using ApexPost.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ApexPost.Infrastructure.Tests.Sql;

[Collection(SqlServerCollection.Name)]
public sealed class StoredProcedureTests(SqlServerTestDatabase database)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Daily_branch_sales_validates_its_parameters_with_meaningful_errors()
    {
        await using var db = database.CreateContext();

        var missing = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"EXEC dbo.usp_DailyBranchSales @BranchCode = {(string?)null}, @Date = {Today}"));
        var unknown = await Assert.ThrowsAsync<RuleViolationException>(() => new EfBranchReportQueries(db).GetDailySalesAsync(new BranchCode("BR-NOWHERE"), Today));

        Assert.Equal(50001, missing.Number);
        Assert.Equal("usp_DailyBranchSales: the branch does not exist.", unknown.Message);
    }

    [Fact]
    public async Task Low_stock_report_covers_one_branch_or_every_branch_when_the_branch_is_null()
    {
        var first = await SqlTestData.SeedAsync(database, stock: 2);
        var second = await SqlTestData.SeedAsync(database, stock: 1);
        await using var db = database.CreateContext();

        var everyBranch = await db.Database.SqlQuery<LowStockRow>($"EXEC dbo.usp_LowStockReport @BranchCode = {(string?)null}, @Threshold = {2}").ToListAsync();
        var oneBranch = await db.Database.SqlQuery<LowStockRow>($"EXEC dbo.usp_LowStockReport @BranchCode = {second.Branch.Value}, @Threshold = {2}").ToListAsync();
        var negative = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"EXEC dbo.usp_LowStockReport @Threshold = {-1}"));

        Assert.Contains(everyBranch, row => row.BranchCode == first.Branch.Value && row.Quantity == 2);
        Assert.Contains(everyBranch, row => row.BranchCode == second.Branch.Value && row.Quantity == 1);
        Assert.Equal([(second.Branch.Value, second.Product.Value, "Test Envelope", 1)], oneBranch.Select(row => (row.BranchCode, row.ProductCode, row.Name, row.Quantity)));
        Assert.Equal(50011, negative.Number);
    }

    [Fact]
    public async Task Adjust_stock_changes_the_row_and_writes_the_history_in_one_transaction()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 5);
        await using var db = database.CreateContext();

        var quantity = await new EfStockAdjustments(db).ApplyAsync(new StockAdjustment(data.Branch, data.Product, 3, "  Delivery received  ", data.Manager), new Sku(data.Product.Value));

        Assert.Equal(8, quantity);
        var history = await db.StockAdjustments.AsNoTracking().SingleAsync(adjustment => adjustment.BranchCode == data.Branch);
        Assert.Equal((3, "Delivery received", data.Manager), (history.Delta, history.Reason, history.ChangedBy));
    }

    [Fact]
    public async Task Adjust_stock_refuses_to_go_below_zero_and_rolls_back_everything()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 2);
        await using var db = database.CreateContext();

        var refused = await Assert.ThrowsAsync<RuleViolationException>(() => new EfStockAdjustments(db).ApplyAsync(new StockAdjustment(data.Branch, data.Product, -3, "Count", data.Manager), new Sku(data.Product.Value)));
        var unknownSku = await Assert.ThrowsAsync<RuleViolationException>(() => new EfStockAdjustments(db).ApplyAsync(new StockAdjustment(data.Branch, data.Product, 1, "Count", data.Manager), new Sku("NO-SUCH-SKU")));
        var zero = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"EXEC dbo.usp_AdjustStock @BranchCode = {data.Branch.Value}, @Sku = {data.Product.Value}, @Delta = {0}, @Reason = {"Count"}, @ChangedBy = {data.Manager.Value}"));

        Assert.Equal("Stock cannot be reduced below zero.", refused.Message);
        Assert.Equal(50028, ((SqlException)refused.InnerException!).Number);
        Assert.Equal("No product has this SKU.", unknownSku.Message);
        Assert.Equal(50023, zero.Number);
        Assert.Equal(2, (await db.BranchStock.AsNoTracking().SingleAsync(item => item.BranchCode == data.Branch)).Quantity);
        Assert.False(await db.StockAdjustments.AnyAsync(adjustment => adjustment.BranchCode == data.Branch));
    }

    [Fact]
    public async Task Adjust_stock_is_refused_unless_an_active_branch_manager_of_the_branch_makes_it()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 5);
        var otherBranch = await SqlTestData.SeedAsync(database, stock: 5);
        var inactiveManager = new StaffId($"OFF-{data.Branch.Value[3..]}");
        await using var db = database.CreateContext();
        var inactive = new StaffMember(inactiveManager, "Former Manager", Domain.Enums.StaffRole.BranchManager, data.Branch);
        inactive.Deactivate();
        db.Staff.Add(inactive);
        await db.SaveChangesAsync();

        // The rule StockAdjustmentService applies also holds for a call that does not come through the app.
        foreach (var changedBy in new[] { data.Clerk, otherBranch.Manager, inactiveManager })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync(
                $"EXEC dbo.usp_AdjustStock @BranchCode = {data.Branch.Value}, @Sku = {data.Product.Value}, @Delta = {1}, @Reason = {"Count"}, @ChangedBy = {changedBy.Value}"));
            Assert.Equal((50025, "A Branch Manager assigned to the branch must make stock adjustments."), (refused.Number, refused.Message));
        }

        Assert.Equal(5, (await db.BranchStock.AsNoTracking().SingleAsync(item => item.BranchCode == data.Branch)).Quantity);
        Assert.False(await db.StockAdjustments.AnyAsync(adjustment => adjustment.BranchCode == data.Branch));
    }

    [Theory]
    [InlineData(StoredProcedureScripts.DailyBranchSales)]
    [InlineData(StoredProcedureScripts.LowStockReport)]
    [InlineData(StoredProcedureScripts.AdjustStock)]
    public async Task The_migrated_database_runs_the_procedure_script_in_source_control(string procedure)
    {
        await using var db = database.CreateContext();

        var deployed = await db.Database.SqlQuery<string>($"SELECT OBJECT_DEFINITION(OBJECT_ID({"dbo." + procedure})) AS [Value]").SingleAsync();

        Assert.Equal(Normalize(StoredProcedureScripts.Read(procedure)), Normalize(deployed));
    }

    // SQL Server stores CREATE OR ALTER as "CREATE   PROCEDURE" (the two words become spaces); the rest is kept verbatim.
    private static string Normalize(string script) =>
        Regex.Replace(script.ReplaceLineEndings("\n").Trim(), @"^CREATE\s+(OR\s+ALTER\s+)?PROCEDURE", "CREATE PROCEDURE",
            RegexOptions.Multiline);

    [Fact]
    public async Task A_procedure_change_invalidates_an_ef_tracked_copy_through_the_rowversion()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 5);
        await using var clerk = database.CreateContext();
        var tracked = (await new EfInventoryRepository(clerk).FindAsync(data.Branch, data.Product))!;

        await using (var manager = database.CreateContext())
        {
            await new EfStockAdjustments(manager).ApplyAsync(new StockAdjustment(data.Branch, data.Product, -5, "Damaged", data.Manager), new Sku(data.Product.Value));
        }

        tracked.RemoveStock(1);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfUnitOfWork(clerk).SaveChangesAsync());
    }

    [Fact]
    public async Task A_first_delivery_inserts_the_stock_row_and_runs_the_procedure_in_one_explicit_transaction()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var newProduct = await AddUnstockedProductAsync(data);
        var log = new SqlCommandLog();
        await using var db = database.CreateContext(interceptors: log);

        var level = await StockAdjustments(db).AdjustAsync(new StockAdjustmentRequest(data.Branch.Value, newProduct.Value, 12, "First delivery"), data.Manager);

        Assert.Equal(12, level.Quantity);
        Assert.Contains(log.Commands, command => command.Contains("INSERT INTO [BranchStock]", StringComparison.Ordinal));
        Assert.Contains(log.Commands, command => command.Contains("usp_AdjustStock", StringComparison.Ordinal));
        Assert.Equal(1, log.Commits);
        await using var check = database.CreateContext();
        Assert.Equal(12, (await check.BranchStock.AsNoTracking().SingleAsync(item => item.BranchCode == data.Branch && item.ProductId == newProduct)).Quantity);
    }

    [Fact]
    public async Task When_the_procedure_fails_the_stock_row_saved_earlier_in_the_transaction_rolls_back_too()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var newProduct = await AddUnstockedProductAsync(data);
        await using var db = database.CreateContext(interceptors: new FailProcedureCall());

        await Assert.ThrowsAnyAsync<Exception>(() => StockAdjustments(db).AdjustAsync(new StockAdjustmentRequest(data.Branch.Value, newProduct.Value, 12, "First delivery"), data.Manager));

        await using var check = database.CreateContext();
        Assert.False(await check.BranchStock.AnyAsync(item => item.BranchCode == data.Branch && item.ProductId == newProduct));
    }

    [Fact]
    public async Task A_rule_thrown_by_the_procedure_inside_the_callers_transaction_rolls_back_the_callers_work()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var newProduct = await AddUnstockedProductAsync(data);
        await using var db = database.CreateContext();
        var unitOfWork = new EfUnitOfWork(db);

        await Assert.ThrowsAsync<RuleViolationException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            new EfInventoryRepository(db).Add(new BranchInventoryItem(data.Branch, newProduct));
            await unitOfWork.SaveChangesAsync(token);
            // The procedure runs inside this transaction (it does not start its own) and refuses to go below zero.
            return await new EfStockAdjustments(db).ApplyAsync(new StockAdjustment(data.Branch, newProduct, -1, "Count", data.Manager), new Sku(newProduct.Value), token);
        }));

        await using var check = database.CreateContext();
        Assert.False(await check.BranchStock.AnyAsync(item => item.BranchCode == data.Branch && item.ProductId == newProduct));
    }

    private async Task<ProductId> AddUnstockedProductAsync(SqlTestData data)
    {
        var productId = new ProductId($"PRD-NEW-{data.Branch.Value[3..]}");
        await using var db = database.CreateContext();
        db.Products.Add(new Product(productId, "New line", new Money(4m)));
        await db.SaveChangesAsync();
        return productId;
    }

    private static StockAdjustmentService StockAdjustments(ApexPostDbContext db)
    {
        var unitOfWork = new EfUnitOfWork(db);
        return new StockAdjustmentService(new EfStaffMemberRepository(db), new EfProductRepository(db), new EfInventoryRepository(db), new EfStockAdjustments(db), unitOfWork,
            new InventoryService(new EfInventoryRepository(db), unitOfWork, new InventoryServiceOptions { LowStockThreshold = 2 }));
    }

    private sealed class LowStockRow
    {
        public string BranchCode { get; init; } = string.Empty;
        public string ProductCode { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Quantity { get; init; }
    }

    /// <summary>Fails the stored procedure call as a database error would, after the earlier SaveChanges has run.</summary>
    private sealed class FailProcedureCall : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            command.CommandText.Contains("usp_AdjustStock", StringComparison.Ordinal)
                ? throw new IOException("Simulated failure while calling the procedure.")
                : ValueTask.FromResult(result);
    }
}
