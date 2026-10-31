using ApexPost.Application.Abstractions;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Configurations;
using ApexPost.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Tests.Sql;

[Collection(SqlServerCollection.Name)]
public sealed class EfRepositoryMappingTests(SqlServerTestDatabase database)
{
    [Fact]
    public async Task Branch_counter_and_staff_round_trip_and_audit_columns_are_stamped_by_the_interceptor()
    {
        var suffix = Unique();
        var actor = new FixedStaff(new StaffId("STF-AUDITOR"));
        await using (var db = database.CreateContext(actor))
        {
            new EfBranchRepository(db).Add(new Branch(new($"BR-{suffix}"), "Harbour Branch", "Coast", "Mon-Fri 09:00-17:00"));
            new EfCounterRepository(db).Add(new Counter(new("C-01"), new($"BR-{suffix}")));
            new EfStaffMemberRepository(db).Add(new StaffMember(new($"STF-{suffix}"), "Harbour Clerk", StaffRole.CounterClerk, new($"BR-{suffix}"), $"clerk.{suffix}@example.test"));
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using (var db = database.CreateContext(actor))
        {
            var branches = new EfBranchRepository(db);
            var branch = (await branches.FindByCodeAsync(new($"br-{suffix.ToLowerInvariant()}")))!;
            Assert.Equal(("Harbour Branch", "Coast", true), (branch.Name, branch.Region, branch.IsOpen));
            Assert.Equal("STF-AUDITOR", db.Entry(branch).Property<string>(ShadowColumns.CreatedBy).CurrentValue);
            Assert.Null(db.Entry(branch).Property<DateTime?>(ShadowColumns.ModifiedAtUtc).CurrentValue);
            Assert.Single(await new EfCounterRepository(db).FindByBranchAsync(branch.Code));
            var staff = (await new EfStaffMemberRepository(db).FindByIdAsync(new($"STF-{suffix}")))!;
            Assert.Equal((StaffRole.CounterClerk, branch.Code, $"clerk.{suffix.ToLowerInvariant()}@example.test"), (staff.Role, staff.AssignedBranchCode, staff.Email));

            branch.Close();
            branches.Update(branch);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var closed = await check.Branches.SingleAsync(branch => branch.Code == new BranchCode($"BR-{suffix}"));
        Assert.False(closed.IsOpen);
        Assert.NotNull(check.Entry(closed).Property<DateTime?>(ShadowColumns.ModifiedAtUtc).CurrentValue);
        Assert.Equal("STF-AUDITOR", check.Entry(closed).Property<string>(ShadowColumns.CreatedBy).CurrentValue);
    }

    [Fact]
    public async Task Product_sku_category_and_shipment_dimensions_round_trip()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        await using (var db = database.CreateContext())
        {
            db.ProductCategories.Add(new ProductCategory($"CAT-{Unique()[..6]}", "Test Category"));
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            new EfShipmentRepository(db).Add(new Shipment(new($"SHP-{data.Branch.Value}"), data.Branch, data.Zone, new ParcelWeight(2.345m), new ParcelDimensions(30.5m, 20m, 10.25m), ShipmentType.Parcel, ShippingServiceLevel.Express, new($"TX-{data.Branch.Value}")));
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var product = (await new EfProductRepository(check).FindByIdAsync(data.Product))!;
        Assert.Equal((data.Product.Value, ProductCategory.GeneralCode, 10m), (product.Sku.Value, product.CategoryCode, product.UnitPrice.Amount));
        var shipment = (await new EfShipmentRepository(check).FindByIdAsync(new($"SHP-{data.Branch.Value}")))!;
        Assert.Equal((2.345m, 30.5m, 20m, 10.25m), (shipment.Weight.Kilograms, shipment.Dimensions.LengthCentimetres, shipment.Dimensions.WidthCentimetres, shipment.Dimensions.HeightCentimetres));
        Assert.Equal((ShipmentType.Parcel, ShippingServiceLevel.Express, $"TX-{data.Branch.Value}"), (shipment.ShipmentType, shipment.ServiceLevel, shipment.TransactionId!.Value));
    }

    [Fact]
    public async Task Money_order_verification_is_added_later_through_the_mapper_and_stored_masked()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var id = new MoneyOrderId($"MO-{data.Branch.Value}");
        await using (var db = database.CreateContext())
        {
            new EfMoneyOrderRepository(db).Add(new MoneyOrder(id, data.Branch, new Money(2500m), "Sender", "Recipient", new Money(27.50m), new($"CTX-{data.Branch.Value}")));
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new EfMoneyOrderRepository(db);
            var order = (await repository.FindByIdAsync(id))!;
            Assert.Equal(IdentityVerificationOutcome.Pending, order.IdentityVerificationOutcome);
            order.RecordIdentityVerification(new IdentityVerificationRecord(order.TransactionContextId, "Passport", MaskedIdentityDocumentReference.FromDocumentNumber("PASS12345678"), IdentityVerificationOutcome.Verified));
            repository.Update(order);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var stored = (await new EfMoneyOrderRepository(check).FindByIdAsync(id))!;
        Assert.Equal((IdentityVerificationOutcome.Verified, "****5678", 2527.50m), (stored.IdentityVerificationOutcome, stored.IdentityVerification!.MaskedDocumentReference.Value, stored.Total.Amount));
        Assert.Equal(0, await check.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM IdentityVerifications WHERE MaskedDocumentReference LIKE '%12345678%'").SingleAsync());
    }

    [Fact]
    public async Task Counter_transaction_with_lines_payment_and_reversal_round_trips()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        var id = new CounterTransactionId($"TX-{data.Branch.Value}");
        await using (var db = database.CreateContext())
        {
            var transaction = new CounterTransaction(id, data.Branch, data.Counter, data.Clerk,
                [new CounterTransactionLine(data.Product, "Test Envelope", 2, new Money(10m)), new CounterTransactionLine(null, "Standard parcel", 1, new Money(8m))],
                new Money(28m), new Money(2.80m), PaymentMethod.DigitalWallet, new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero));
            transaction.MarkPaid();
            new EfCounterTransactionRepository(db).Add(transaction);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var repository = new EfCounterTransactionRepository(db);
            var transaction = (await repository.FindByIdAsync(id))!;
            transaction.Refund(data.Manager, "Customer returned it");
            repository.Update(transaction);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using var check = database.CreateContext();
        var stored = Assert.Single(await new EfCounterTransactionRepository(check).FindByBranchAsync(data.Branch));
        Assert.Equal((CounterTransactionStatus.Refunded, data.Manager, 30.80m), (stored.Status, stored.AuthorizedBy, stored.Total.Amount));
        Assert.Equal((PaymentMethod.DigitalWallet, 30.80m), (stored.Payment.Method, stored.Payment.Amount.Amount));
        Assert.Equal([(true, 2, 20m), (false, 1, 8m)], stored.Lines.Select(line => (line.IsRetail, line.Quantity, line.Total.Amount)).OrderByDescending(line => line.IsRetail));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero), stored.OccurredAtUtc);
    }

    [Fact]
    public async Task Value_object_predicates_translate_to_parameterized_sql_on_the_varchar_column()
    {
        await using var db = database.CreateContext();
        var code = new BranchCode("BR-NORTH");

        var sql = db.Branches.Where(branch => branch.Code == code).ToQueryString();

        // ToQueryString shows the parameter value in a DECLARE line; the query itself only references the parameter,
        // typed varchar(48) like the column, so there is no implicit conversion and no literal in the WHERE clause.
        var lines = sql.Split('\n', StringSplitOptions.TrimEntries);
        Assert.Contains(lines, line => line.StartsWith("DECLARE @", StringComparison.Ordinal) && line.Contains("varchar(48) = 'BR-NORTH'", StringComparison.Ordinal));
        var where = Assert.Single(lines, line => line.StartsWith("WHERE", StringComparison.Ordinal));
        Assert.Matches(@"^WHERE \[b\]\.\[BranchCode\] = @\w+$", where);
    }

    [Fact]
    public async Task A_stale_stock_read_is_rejected_as_a_concurrency_conflict()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstItem = (await new EfInventoryRepository(first).FindAsync(data.Branch, data.Product))!;
        var secondItem = (await new EfInventoryRepository(second).FindAsync(data.Branch, data.Product))!;
        firstItem.RemoveStock(1);
        secondItem.RemoveStock(1);

        await new EfUnitOfWork(first).SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => new EfUnitOfWork(second).SaveChangesAsync());
    }

    [Fact]
    public async Task A_duplicate_business_key_is_reported_as_a_duplicate_key()
    {
        var suffix = Unique();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var db = database.CreateContext();
            new EfBranchRepository(db).Add(new Branch(new($"BR-{suffix}"), "Duplicate Branch"));
            if (attempt == 0) await new EfUnitOfWork(db).SaveChangesAsync();
            else await Assert.ThrowsAsync<DuplicateKeyException>(() => new EfUnitOfWork(db).SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Updating_an_aggregate_that_was_not_loaded_for_change_is_refused()
    {
        var data = await SqlTestData.SeedAsync(database, stock: 1);
        await using var db = database.CreateContext();
        var repository = new EfBranchRepository(db);
        var listed = (await repository.GetAllAsync()).Single(branch => branch.Code == data.Branch);

        listed.Close();

        Assert.Throws<InvalidOperationException>(() => repository.Update(listed));
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private sealed class FixedStaff(StaffId staffId) : ICurrentStaffContext
    {
        public StaffId? StaffId => staffId;
        public BranchCode? BranchCode => null;
    }
}
