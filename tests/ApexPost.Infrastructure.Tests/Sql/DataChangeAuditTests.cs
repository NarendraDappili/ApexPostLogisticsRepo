using ApexPost.Application.Abstractions;
using ApexPost.Application.Auditing;
using ApexPost.Application.Catalogue;
using ApexPost.Application.Inventory;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Auditing;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using ApexPost.Infrastructure.Persistence.StoredProcedures;
using ApexPost.Infrastructure.Queries;
using ApexPost.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ApexPost.Infrastructure.Tests.Sql;

/// <summary>
/// The DataChanges history as the web app writes it: the interceptor for tracked saves, and the audited decorators
/// for the two writes that bypass SaveChanges. Every test reads the rows back by its own correlation ID.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DataChangeAuditTests(SqlServerTestDatabase database)
{
    [Fact]
    public async Task A_new_staff_member_is_an_insert_named_by_its_business_key_with_the_password_hash_hidden()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var correlation = Correlation();
        var staffId = new StaffId($"STF-NEW-{Suffix()}");
        await using (var db = Context(correlation, new StaffId("STF-ADMIN-01")))
        {
            var member = new StaffMember(staffId, "New Clerk", StaffRole.CounterClerk, data.Branch, $"{staffId.Value.ToLowerInvariant()}@example.test");
            member.SetPasswordHash("PBKDF2-SHA256$not-a-real-secret-hash");
            db.Staff.Add(member);
            await db.SaveChangesAsync();
        }

        var change = Assert.Single(await ChangesAsync(correlation));
        Assert.Equal(("Staff", $"StaffCode={staffId.Value}", "Insert", "STF-ADMIN-01", data.Branch.Value), (change.TableName, change.RecordKey, change.Operation, change.StaffId, change.BranchCode));
        var columns = DataChangeJson.Deserialize(change.ChangesJson).ToDictionary(column => column.Column, column => column.New);
        Assert.Equal("New Clerk", columns["Name"]);
        Assert.Equal("CounterClerk", columns["RoleId"]);
        Assert.Equal(DataChangeColumn.Hidden, columns["PasswordHash"]);
        Assert.DoesNotContain("not-a-real-secret-hash", change.ChangesJson, StringComparison.Ordinal);
        // Surrogate identities and the audit columns are not history.
        Assert.DoesNotContain("StaffMemberId", columns.Keys);
        Assert.DoesNotContain("CreatedAtUtc", columns.Keys);
        await using var check = database.CreateContext();
        Assert.Equal("STF-ADMIN-01", await check.Database.SqlQuery<string>($"SELECT CreatedBy AS [Value] FROM DataChanges WHERE CorrelationId = {correlation}").SingleAsync());
    }

    [Fact]
    public async Task An_update_records_only_the_columns_that_changed_with_their_old_and_new_values()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var correlation = Correlation();
        await using (var db = Context(correlation))
        {
            var branch = await db.Branches.SingleAsync(candidate => candidate.Code == data.Branch);
            branch.UpdateDetails("Renamed Branch", "Test Region", "Mon-Fri");
            await db.SaveChangesAsync();
            // Saving again with nothing changed writes no history.
            await db.SaveChangesAsync();
        }

        var change = Assert.Single(await ChangesAsync(correlation));
        Assert.Equal(("Branches", $"BranchCode={data.Branch.Value}", "Update", data.Branch.Value), (change.TableName, change.RecordKey, change.Operation, change.BranchCode));
        Assert.Equal([new DataChangeColumn("Name", "Test Branch", "Renamed Branch")], DataChangeJson.Deserialize(change.ChangesJson));
    }

    [Fact]
    public async Task A_sale_records_the_transaction_its_line_its_payment_and_the_stock_it_took_in_one_commit()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 20);
        var correlation = Correlation();
        var transactionId = $"TX-AUD-{Suffix()}";
        await using (var db = Context(correlation, data.Clerk))
        {
            await SqlTestData.CheckoutService(db).CheckoutAsync(data.Sale(transactionId, 2));
        }

        var changes = await ChangesAsync(correlation);
        Assert.All(changes, change => Assert.Equal((data.Branch.Value, data.Clerk.Value), (change.BranchCode, change.StaffId)));
        Assert.Contains(changes, change => change is { TableName: "CounterTransactions", Operation: "Insert" } && change.RecordKey == $"TransactionCode={transactionId}");
        // A child row with only an identity key is named after its parent.
        Assert.Contains(changes, change => change is { TableName: "TransactionLines", Operation: "Insert" } && change.RecordKey == $"TransactionCode={transactionId} / TransactionLines");
        Assert.Contains(changes, change => change is { TableName: "Payments", Operation: "Insert" } && change.RecordKey == $"TransactionCode={transactionId} / Payments");
        var stock = Assert.Single(changes, change => change.TableName == "BranchStock");
        Assert.Equal($"BranchCode={data.Branch.Value}, ProductCode={data.Product.Value}", stock.RecordKey);
        Assert.Equal([new DataChangeColumn("Quantity", "20", "18")], DataChangeJson.Deserialize(stock.ChangesJson));
    }

    [Fact]
    public async Task A_shipments_dimensions_are_part_of_the_shipment_row_change()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var correlation = Correlation();
        var shipmentId = new ShipmentId($"SHP-AUD-{Suffix()}");
        await using (var db = Context(correlation, data.Clerk))
        {
            db.Shipments.Add(new Shipment(shipmentId, data.Branch, data.Zone, new ParcelWeight(2m), new ParcelDimensions(10m, 20m, 30m)));
            await db.SaveChangesAsync();
        }

        var change = Assert.Single(await ChangesAsync(correlation));
        Assert.Equal(("Shipments", $"ShipmentCode={shipmentId.Value}", data.Branch.Value), (change.TableName, change.RecordKey, change.BranchCode));
        var columns = DataChangeJson.Deserialize(change.ChangesJson).ToDictionary(column => column.Column, column => column.New);
        Assert.Equal(("10.00", "20.00", "30.00"), (columns["LengthCm"], columns["WidthCm"], columns["HeightCm"]));
        Assert.Equal(data.Branch.Value, columns["OriginBranchCode"]);
    }

    [Fact]
    public async Task A_failed_save_keeps_no_history_and_saving_again_does_not_double_it()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var correlation = Correlation();
        var newBranch = new BranchCode($"BR-OK-{Suffix()}");
        await using (var db = Context(correlation))
        {
            db.Branches.Add(new Branch(newBranch, "New Branch"));
            var duplicate = db.Branches.Add(new Branch(data.Branch, "Duplicate Branch"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Empty(await ChangesAsync(correlation));

            duplicate.State = EntityState.Detached;
            await db.SaveChangesAsync();
        }

        var change = Assert.Single(await ChangesAsync(correlation));
        Assert.Equal($"BranchCode={newBranch.Value}", change.RecordKey);
    }

    [Fact]
    public async Task A_change_made_without_a_signed_in_staff_member_is_attributed_to_the_system()
    {
        var correlation = Correlation();
        var productId = new ProductId($"PRD-SYS-{Suffix()}");
        await using (var db = database.CreateContext(interceptors: new DataChangeAuditInterceptor(TimeProvider.System, correlation: new FixedCorrelation(correlation))))
        {
            db.Products.Add(new Product(productId, "Seeded Product", new Money(1.25m)));
            await db.SaveChangesAsync();
        }

        var change = Assert.Single(await ChangesAsync(correlation));
        Assert.Null(change.StaffId);
        Assert.Null(change.BranchCode);
        Assert.Contains(new DataChangeColumn("UnitPrice", null, "1.25"), DataChangeJson.Deserialize(change.ChangesJson));
        await using var check = database.CreateContext();
        Assert.Equal("system", await check.Database.SqlQuery<string>($"SELECT CreatedBy AS [Value] FROM DataChanges WHERE CorrelationId = {correlation}").SingleAsync());
    }

    [Fact]
    public async Task The_price_revision_stays_one_update_and_records_each_repriced_product_with_its_old_and_new_price()
    {
        var suffix = Suffix();
        var category = $"CAT-{suffix}";
        await using (var setup = database.CreateContext())
        {
            setup.ProductCategories.Add(new ProductCategory(category, "Audited Category"));
            setup.Products.AddRange(
                new Product(new($"PRD-A-{suffix}"), "Audited A", new Money(10m), categoryCode: category),
                new Product(new($"PRD-B-{suffix}"), "Audited B", new Money(3.33m), categoryCode: category),
                new Product(new($"PRD-C-{suffix}"), "Untouched C", new Money(10m)));
            await setup.SaveChangesAsync();
        }

        var correlation = Correlation();
        var log = new SqlCommandLog();
        await using (var db = Context(correlation, new StaffId("STF-ADMIN-01"), log))
        {
            Assert.Equal(2, await new PriceRevisionService(Revisions(db)).ReviseAsync(new PriceRevisionRequest(category, 5m)));
        }

        Assert.Single(log.Commands, command => command.TrimStart().StartsWith("UPDATE [p]", StringComparison.Ordinal));
        var changes = (await ChangesAsync(correlation)).OrderBy(change => change.RecordKey).ToArray();
        Assert.Equal([$"ProductCode=PRD-A-{suffix}", $"ProductCode=PRD-B-{suffix}"], changes.Select(change => change.RecordKey));
        Assert.Equal([new DataChangeColumn("UnitPrice", "10.00", "10.50")], DataChangeJson.Deserialize(changes[0].ChangesJson));
        Assert.Equal([new DataChangeColumn("UnitPrice", "3.33", "3.50")], DataChangeJson.Deserialize(changes[1].ChangesJson));
        Assert.All(changes, change => Assert.Equal(("Products", "Update", "STF-ADMIN-01"), (change.TableName, change.Operation, change.StaffId)));
    }

    [Fact]
    public async Task A_price_revision_rolled_back_by_its_caller_leaves_neither_the_new_prices_nor_their_history()
    {
        var suffix = Suffix();
        var category = $"CAT-{suffix}";
        var productId = new ProductId($"PRD-RB-{suffix}");
        await using (var setup = database.CreateContext())
        {
            setup.ProductCategories.Add(new ProductCategory(category, "Rolled Back Category"));
            setup.Products.Add(new Product(productId, "Rolled Back", new Money(10m), categoryCode: category));
            await setup.SaveChangesAsync();
        }

        var correlation = Correlation();
        await using (var db = Context(correlation))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            // The decorator joins the caller's transaction instead of opening its own.
            Assert.Equal(1, await Revisions(db).RevisePricesAsync(category, 2m));
            await transaction.RollbackAsync();
        }

        Assert.Empty(await ChangesAsync(correlation));
        await using var check = database.CreateContext();
        Assert.Equal(10m, (await check.Products.AsNoTracking().SingleAsync(product => product.Id == productId)).UnitPrice.Amount);
    }

    [Fact]
    public async Task A_stock_adjustment_records_the_quantity_change_and_its_history_row_and_a_refused_one_records_nothing()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 5);
        var correlation = Correlation();
        await using (var db = Context(correlation, data.Manager))
        {
            var level = await StockAdjustments(db).AdjustAsync(new StockAdjustmentRequest(data.Branch.Value, data.Product.Value, 3, "Delivery received"), data.Manager);
            Assert.Equal(8, level.Quantity);
        }

        var changes = await ChangesAsync(correlation);
        var stock = Assert.Single(changes, change => change.TableName == "BranchStock");
        Assert.Equal([new DataChangeColumn("Quantity", "5", "8")], DataChangeJson.Deserialize(stock.ChangesJson));
        var history = Assert.Single(changes, change => change.TableName == "StockAdjustments");
        Assert.Equal("Insert", history.Operation);
        Assert.Contains(new DataChangeColumn("Reason", null, "Delivery received"), DataChangeJson.Deserialize(history.ChangesJson));
        Assert.All(changes, change => Assert.Equal((data.Manager.Value, data.Branch.Value, correlation), (change.StaffId, change.BranchCode, change.CorrelationId)));

        var refused = Correlation();
        await using (var db = Context(refused, data.Manager))
        {
            await Assert.ThrowsAsync<RuleViolationException>(() => StockAdjustments(db).AdjustAsync(new StockAdjustmentRequest(data.Branch.Value, data.Product.Value, -100, "Count"), data.Manager));
        }

        Assert.Empty(await ChangesAsync(refused));
    }

    [Fact]
    public async Task Every_saved_change_reaches_the_audit_log_with_its_column_names_but_never_their_values()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var correlation = Correlation();
        var logs = new CapturingLoggerFactory();
        await using (var db = database.CreateContext(interceptors: new DataChangeAuditInterceptor(TimeProvider.System, new FixedStaff(data.Manager, data.Branch), new FixedCorrelation(correlation), new AuditTrailLogger(logs))))
        {
            var branch = await db.Branches.SingleAsync(candidate => candidate.Code == data.Branch);
            branch.UpdateDetails("Confidential New Name", "Test Region", "Mon-Fri");
            await db.SaveChangesAsync();
        }

        var message = Assert.Single(logs.Messages, message => message.Contains(correlation, StringComparison.Ordinal));
        Assert.Equal(AuditTrailLogger.Category, logs.Categories.Single());
        Assert.Contains($"Audit data change Update Branches BranchCode={data.Branch.Value} columns [Name] by {data.Manager.Value}", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidential New Name", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_security_event_is_stored_with_overlong_values_cut_to_their_columns_and_written_to_the_audit_log()
    {
        var correlation = Correlation();
        var logs = new CapturingLoggerFactory();
        var record = new SecurityEventRecord(DateTimeOffset.UtcNow, SecurityEventType.SignInFailed, "STF-CLERK-01", null, null, "POST",
            "/" + new string('a', 400), 200, "127.0.0.1", "WrongPassword", correlation);

        await new EfSecurityEventStore(database.CreateOptions(), new AuditTrailLogger(logs)).AppendAsync(record);

        await using var check = database.CreateContext();
        var stored = await check.SecurityEvents.AsNoTracking().SingleAsync(row => row.CorrelationId == correlation);
        Assert.Equal(("SignInFailed", "STF-CLERK-01", 256, "WrongPassword"), (stored.EventType, stored.StaffId, stored.Path.Length, stored.Detail));
        Assert.Contains(logs.Messages, message => message.StartsWith("Audit security SignInFailed STF-CLERK-01 POST", StringComparison.Ordinal) && message.Contains(correlation, StringComparison.Ordinal));
    }

    private ApexPostDbContext Context(string correlationId, StaffId? staffId = null, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] more)
    {
        var staff = new FixedStaff(staffId ?? new StaffId("STF-TEST-ACTOR"), null);
        return database.CreateContext(staff, null, [new DataChangeAuditInterceptor(TimeProvider.System, staff, new FixedCorrelation(correlationId)), .. more]);
    }

    private async Task<List<DataChangeEntity>> ChangesAsync(string correlationId)
    {
        await using var check = database.CreateContext();
        return await check.DataChanges.AsNoTracking().Where(change => change.CorrelationId == correlationId).ToListAsync();
    }

    private static AuditedCatalogueBulkUpdates Revisions(ApexPostDbContext db) => new(new EfCatalogueBulkUpdates(db, TimeProvider.System), db);

    private static StockAdjustmentService StockAdjustments(ApexPostDbContext db)
    {
        var unitOfWork = new EfUnitOfWork(db);
        return new StockAdjustmentService(new EfStaffMemberRepository(db), new EfProductRepository(db), new EfInventoryRepository(db), new AuditedStockAdjustments(new EfStockAdjustments(db), db), unitOfWork,
            new InventoryService(new EfInventoryRepository(db), unitOfWork, new InventoryServiceOptions { LowStockThreshold = 2 }));
    }

    private static string Correlation() => $"corr-{Guid.NewGuid():N}";

    private static string Suffix() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private sealed class FixedStaff(StaffId staffId, BranchCode? branchCode) : ICurrentStaffContext
    {
        public StaffId? StaffId => staffId;
        public BranchCode? BranchCode => branchCode;
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public string? CorrelationId => correlationId;
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _categories = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();
        public IReadOnlyCollection<string> Categories => _categories.Keys.ToArray();

        public ILogger CreateLogger(string categoryName)
        {
            _categories.TryAdd(categoryName, 0);
            return new CapturingLogger(_messages);
        }

        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        private sealed class CapturingLogger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
