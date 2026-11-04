using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Persistence.Entities;
using ApexPost.Infrastructure.Queries;

namespace ApexPost.Infrastructure.Tests.Sql;

/// <summary>
/// The audit log's read side against SQL Server. Each test writes rows for its own branch code and period, so the
/// shared test database's other rows never match its filters.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuditLogQueryTests(SqlServerTestDatabase database)
{
    private static readonly DateOnly Day = new(2031, 3, 15);

    [Fact]
    public async Task The_period_includes_both_days_whole_and_nothing_either_side()
    {
        var branch = Branch();
        await SeedRequestsAsync(branch, ("2031-03-13T23:59:59Z", "STF-A"), ("2031-03-14T00:00:00Z", "STF-A"), ("2031-03-15T23:59:59Z", "STF-A"), ("2031-03-16T00:00:00Z", "STF-A"));

        var page = await Queries().GetRequestsAsync(new AuditLogFilter(Day.AddDays(-1), Day, BranchCode: branch));

        Assert.Equal(["2031-03-15 23:59:59", "2031-03-14 00:00:00"], page.Items.Select(item => item.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Requests_are_filtered_by_staff_controller_outcome_and_correlation_and_listed_newest_first()
    {
        var branch = Branch();
        await SeedRequestsAsync(branch, ("2031-03-15T08:00:00Z", "STF-A"), ("2031-03-15T09:00:00Z", "STF-B"), ("2031-03-15T10:00:00Z", "STF-A"));
        var queries = Queries();

        var byStaff = await queries.GetRequestsAsync(new AuditLogFilter(Day, Day, BranchCode: branch, StaffId: "STF-A"));
        var failed = await queries.GetRequestsAsync(new AuditLogFilter(Day, Day, BranchCode: branch, Kind: "StaffAdmin", Result: "Failed"));
        var traced = await queries.GetRequestsAsync(new AuditLogFilter(Day, Day, BranchCode: branch, CorrelationId: byStaff.Items[0].CorrelationId));

        Assert.Equal(2, byStaff.TotalCount);
        Assert.True(byStaff.Items[0].OccurredAt > byStaff.Items[1].OccurredAt);
        // The seed alternates: the second request is a failed StaffAdmin/Edit.
        Assert.Equal(("STF-B", "StaffAdmin", "Failed", 409), (failed.Items.Single().StaffId, failed.Items.Single().Controller, failed.Items.Single().Outcome, failed.Items.Single().StatusCode));
        Assert.Equal("TX-1", Assert.Single(traced.Items).RouteValues["id"]);
    }

    [Fact]
    public async Task Pages_hold_the_page_size_and_a_page_past_the_end_shows_the_last_one()
    {
        var branch = Branch();
        await SeedRequestsAsync(branch, [.. Enumerable.Range(0, 7).Select(minute => ($"2031-03-15T10:{minute:00}:00Z", "STF-A"))]);
        var queries = Queries();

        var second = await queries.GetRequestsAsync(new AuditLogFilter(Day, Day, BranchCode: branch, Page: 2, PageSize: 3));
        var beyond = await queries.GetRequestsAsync(new AuditLogFilter(Day, Day, BranchCode: branch, Page: 9, PageSize: 3));

        Assert.Equal((7, 3, 3, 2), (second.TotalCount, second.TotalPages, second.Items.Count, second.Page));
        Assert.Equal((3, 1), (beyond.Page, beyond.Items.Count));
    }

    [Fact]
    public async Task Data_changes_are_filtered_by_table_and_operation_and_read_back_with_their_column_changes()
    {
        var branch = Branch();
        await using (var db = database.CreateContext())
        {
            db.DataChanges.AddRange(
                Change(branch, "Staff", "Update", """[{"column":"Name","old":"A","new":"B"}]"""),
                Change(branch, "Staff", "Insert", """[{"column":"Name","old":null,"new":"C"}]"""),
                Change(branch, "Products", "Update", """[{"column":"UnitPrice","old":"1.00","new":"1.10"}]"""));
            await db.SaveChangesAsync();
        }

        var page = await Queries().GetDataChangesAsync(new AuditLogFilter(Day, Day, BranchCode: branch, Kind: "Staff", Result: "Update"));

        var item = Assert.Single(page.Items);
        Assert.Equal((DataChangeOperation.Update, "Staff"), (item.Operation, item.TableName));
        Assert.Equal([new DataChangeColumn("Name", "A", "B")], item.Columns);
    }

    [Fact]
    public async Task Security_events_are_filtered_by_event_type_and_staff()
    {
        var branch = Branch();
        await using (var db = database.CreateContext())
        {
            db.SecurityEvents.AddRange(Event(branch, "SignInFailed", "STF-A"), Event(branch, "SignInFailed", "STF-B"), Event(branch, "AccessDenied", "STF-A"));
            await db.SaveChangesAsync();
        }

        var page = await Queries().GetSecurityEventsAsync(new AuditLogFilter(Day, Day, BranchCode: branch, StaffId: "STF-A", Kind: "SignInFailed"));

        var item = Assert.Single(page.Items);
        Assert.Equal((SecurityEventType.SignInFailed, "STF-A", "/Account/Login"), (item.EventType, item.StaffId, item.Path));
    }

    [Fact]
    public async Task A_trace_finds_one_requests_rows_in_all_three_logs_and_a_branch_scope_hides_the_others()
    {
        var branch = Branch();
        var otherBranch = Branch();
        var correlation = $"corr-{Guid.NewGuid():N}";
        await using (var db = database.CreateContext())
        {
            db.AuditEntries.Add(Request(branch, "2031-03-15T10:00:00Z", "STF-A", correlation));
            db.DataChanges.AddRange(Change(branch, "Staff", "Update", "[]", correlation), Change(otherBranch, "Staff", "Update", "[]", correlation));
            db.SecurityEvents.Add(Event(branch, "SignInSucceeded", "STF-A", correlation));
            await db.SaveChangesAsync();
        }
        var queries = Queries();

        var everything = await queries.GetTraceAsync(correlation, branchCode: null, includeSecurityEvents: true, limit: 50);
        var ownBranch = await queries.GetTraceAsync(correlation, branch, includeSecurityEvents: false, limit: 50);

        Assert.Equal((1, 2, 1), (everything.Requests.Count, everything.DataChanges.Count, everything.SecurityEvents.Count));
        Assert.Equal((1, 1, 0), (ownBranch.Requests.Count, ownBranch.DataChanges.Count, ownBranch.SecurityEvents.Count));
        Assert.Equal(branch, ownBranch.DataChanges.Single().BranchCode);
    }

    [Fact]
    public void The_table_filter_lists_every_business_table_but_not_the_audit_tables_themselves()
    {
        using var db = database.CreateContext();

        var tables = new EfAuditLogQueries(db).AuditedTables;

        Assert.Contains("Staff", tables);
        Assert.Contains("TransactionLines", tables);
        Assert.DoesNotContain("AuditEntries", tables);
        Assert.DoesNotContain("DataChanges", tables);
        Assert.DoesNotContain("SecurityEvents", tables);
        Assert.Equal(tables.Order(StringComparer.Ordinal), tables);
    }

    private EfAuditLogQueries Queries() => new(database.CreateContext());

    private async Task SeedRequestsAsync(string branch, params (string At, string Staff)[] requests)
    {
        await using var db = database.CreateContext();
        for (var index = 0; index < requests.Length; index++)
        {
            var entry = Request(branch, requests[index].At, requests[index].Staff, $"corr-{Guid.NewGuid():N}");
            if (index % 2 == 1) (entry.Controller, entry.Action, entry.StatusCode, entry.Outcome) = ("StaffAdmin", "Edit", 409, "Failed");
            db.AuditEntries.Add(entry);
        }

        await db.SaveChangesAsync();
    }

    private static AuditEntryEntity Request(string branch, string at, string staff, string correlation) => new()
    {
        OccurredAtUtc = DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture),
        StaffId = staff, Role = "BranchManager", BranchCode = branch, Controller = "CounterTransactions", Action = "Void",
        RouteValuesJson = """{"id":"TX-1"}""", StatusCode = 302, DurationMilliseconds = 5, CorrelationId = correlation, Outcome = "Succeeded",
    };

    private static DataChangeEntity Change(string branch, string table, string operation, string changesJson, string? correlation = null) => new()
    {
        OccurredAtUtc = new DateTimeOffset(2031, 3, 15, 12, 0, 0, TimeSpan.Zero), StaffId = "STF-A", BranchCode = branch,
        TableName = table, RecordKey = $"{table}Code=X", Operation = operation, ChangesJson = changesJson, CorrelationId = correlation ?? $"corr-{Guid.NewGuid():N}",
    };

    private static SecurityEventEntity Event(string branch, string eventType, string staff, string? correlation = null) => new()
    {
        OccurredAtUtc = new DateTimeOffset(2031, 3, 15, 12, 0, 0, TimeSpan.Zero), EventType = eventType, StaffId = staff, BranchCode = branch,
        HttpMethod = "POST", Path = "/Account/Login", StatusCode = 200, CorrelationId = correlation ?? $"corr-{Guid.NewGuid():N}",
    };

    private static string Branch() => $"BR-LOG-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
