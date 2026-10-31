using ApexPost.Application.Abstractions;
using ApexPost.Application.Auditing;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Auditing;
using ApexPost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Infrastructure.Tests.Sql;

[Collection(SqlServerCollection.Name)]
public sealed class EfAuditStoreTests(SqlServerTestDatabase database)
{
    [Fact]
    public async Task An_audit_record_round_trips_with_its_route_values_and_is_stamped_with_the_signed_in_staff()
    {
        var correlation = Correlation();
        var options = database.CreateOptions(new FixedStaff(new StaffId("STF-MANAGER-01")));

        await new EfAuditStore(options).AppendAsync(Record(correlation, "Succeeded", 302, new Dictionary<string, string> { ["transactionId"] = "TX-1" }));

        var stored = Assert.Single(await new EfAuditStore(options).ReadAsync(), record => record.CorrelationId == correlation);
        Assert.Equal(("STF-MANAGER-01", "BranchManager", "CounterTransactions", "Void", 302, "Succeeded"), (stored.StaffId, stored.Role, stored.Controller, stored.Action, stored.StatusCode, stored.Outcome));
        Assert.Equal("TX-1", stored.RouteValues["transactionId"]);
        await using var check = database.CreateContext();
        Assert.Equal("STF-MANAGER-01", await check.Database.SqlQuery<string>($"SELECT CreatedBy AS [Value] FROM AuditEntries WHERE CorrelationId = {correlation}").SingleAsync());
    }

    [Fact]
    public async Task A_failed_business_save_is_audited_but_never_committed_by_the_audit_write()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var options = database.CreateOptions();
        await using (var setup = new ApexPostDbContext(options))
        {
            setup.Branches.Add(new Branch(new($"BR-{suffix}"), "Existing Branch"));
            await setup.SaveChangesAsync();
        }

        // The request's unit of work fails: the branch code is already taken.
        await using var request = new ApexPostDbContext(options);
        request.Branches.Add(new Branch(new($"BR-{suffix}"), "Duplicate Branch"));
        await Assert.ThrowsAsync<DuplicateKeyException>(() => new EfUnitOfWork(request).SaveChangesAsync());

        var correlation = Correlation();
        await new EfAuditStore(options).AppendAsync(Record(correlation, "Failed", 409));

        await using var check = database.CreateContext();
        Assert.True(await check.AuditEntries.AnyAsync(entry => entry.CorrelationId == correlation && entry.Outcome == "Failed"));
        Assert.Equal(1, await check.Branches.CountAsync(branch => branch.Code == new BranchCode($"BR-{suffix}")));
        // The failed change is still only staged in the request's context; the audit write did not touch it.
        Assert.Single(request.ChangeTracker.Entries<Branch>(), entry => entry.State == EntityState.Added);
    }

    [Fact]
    public async Task A_failed_audit_write_does_not_undo_a_committed_business_change()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var options = database.CreateOptions();
        await using (var request = new ApexPostDbContext(options))
        {
            request.Branches.Add(new Branch(new($"BR-{suffix}"), "Committed Branch"));
            await new EfUnitOfWork(request).SaveChangesAsync();
        }

        // A status code outside 100-599 breaks CK_AuditEntries_StatusCode, so the audit insert fails in the database.
        await Assert.ThrowsAsync<DbUpdateException>(() => new EfAuditStore(options).AppendAsync(Record(Correlation(), "Failed", 700)));

        await using var check = database.CreateContext();
        Assert.True(await check.Branches.AnyAsync(branch => branch.Code == new BranchCode($"BR-{suffix}")));
    }

    private static string Correlation() => $"corr-{Guid.NewGuid():N}";

    private static AuditRecord Record(string correlationId, string outcome, int statusCode, IReadOnlyDictionary<string, string>? routeValues = null) =>
        new(DateTimeOffset.UtcNow, "STF-MANAGER-01", "BranchManager", "BR-NORTH", "CounterTransactions", "Void",
            routeValues ?? new Dictionary<string, string>(), statusCode, 12, correlationId, outcome);

    private sealed class FixedStaff(StaffId staffId) : ICurrentStaffContext
    {
        public StaffId? StaffId => staffId;
        public BranchCode? BranchCode => null;
    }
}
