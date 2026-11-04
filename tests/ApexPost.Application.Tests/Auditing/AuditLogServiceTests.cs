using ApexPost.Application.Auditing;
using ApexPost.Domain.Enums;

namespace ApexPost.Application.Tests.Auditing;

/// <summary>Who may read which part of the audit log, decided on the server whatever the request asks for.</summary>
public sealed class AuditLogServiceTests
{
    private static readonly DateOnly Day = new(2026, 11, 4);

    [Theory]
    [InlineData(StaffRole.Auditor)]
    [InlineData(StaffRole.Administrator)]
    public async Task Auditors_and_administrators_read_every_branch_or_the_one_they_choose(StaffRole role)
    {
        var queries = new RecordingQueries();
        var service = new AuditLogService(queries);
        var viewer = new AuditViewer(role, "BR-NORTH");

        await service.GetRequestsAsync(viewer, new AuditLogFilter(Day, Day));
        await service.GetDataChangesAsync(viewer, new AuditLogFilter(Day, Day, BranchCode: "br-south"));
        await service.GetSecurityEventsAsync(viewer, new AuditLogFilter(Day, Day));

        Assert.Equal([null, "BR-SOUTH", null], queries.Filters.Select(filter => filter.BranchCode));
    }

    [Fact]
    public async Task A_branch_manager_is_kept_to_their_own_branch_even_without_choosing_one()
    {
        var queries = new RecordingQueries();
        var viewer = new AuditViewer(StaffRole.BranchManager, "br-north");

        await new AuditLogService(queries).GetRequestsAsync(viewer, new AuditLogFilter(Day, Day));
        await new AuditLogService(queries).GetDataChangesAsync(viewer, new AuditLogFilter(Day, Day, BranchCode: " BR-NORTH "));

        Assert.All(queries.Filters, filter => Assert.Equal("BR-NORTH", filter.BranchCode));
    }

    [Fact]
    public async Task A_branch_manager_asking_for_another_branch_is_refused_not_quietly_narrowed()
    {
        var queries = new RecordingQueries();
        var viewer = new AuditViewer(StaffRole.BranchManager, "BR-NORTH");

        await Assert.ThrowsAsync<AuditLogAccessDeniedException>(() => new AuditLogService(queries).GetRequestsAsync(viewer, new AuditLogFilter(Day, Day, BranchCode: "BR-SOUTH")));

        Assert.Empty(queries.Filters);
    }

    [Fact]
    public async Task A_branch_manager_never_reads_security_events_and_traces_only_their_branch_without_them()
    {
        var queries = new RecordingQueries();
        var service = new AuditLogService(queries);
        var viewer = new AuditViewer(StaffRole.BranchManager, "BR-NORTH");

        await Assert.ThrowsAsync<AuditLogAccessDeniedException>(() => service.GetSecurityEventsAsync(viewer, new AuditLogFilter(Day, Day)));
        await service.GetTraceAsync(viewer, " corr-1 ");

        Assert.Equal(("corr-1", "BR-NORTH", false, AuditLogService.TraceLimit), queries.Trace);
    }

    [Fact]
    public async Task An_auditor_traces_a_request_across_every_branch_with_its_security_events()
    {
        var queries = new RecordingQueries();

        await new AuditLogService(queries).GetTraceAsync(new AuditViewer(StaffRole.Auditor, "BR-NORTH"), "corr-2");

        Assert.Equal(("corr-2", null, true, AuditLogService.TraceLimit), queries.Trace);
    }

    [Fact]
    public async Task A_counter_clerk_cannot_read_any_part_of_the_audit_log()
    {
        var queries = new RecordingQueries();
        var service = new AuditLogService(queries);
        var clerk = new AuditViewer(StaffRole.CounterClerk, "BR-NORTH");

        await Assert.ThrowsAsync<AuditLogAccessDeniedException>(() => service.GetRequestsAsync(clerk, new AuditLogFilter(Day, Day)));
        await Assert.ThrowsAsync<AuditLogAccessDeniedException>(() => service.GetDataChangesAsync(clerk, new AuditLogFilter(Day, Day)));
        await Assert.ThrowsAsync<AuditLogAccessDeniedException>(() => service.GetSecurityEventsAsync(clerk, new AuditLogFilter(Day, Day)));
        await Assert.ThrowsAsync<AuditLogAccessDeniedException>(() => service.GetTraceAsync(clerk, "corr-3"));

        Assert.Empty(queries.Filters);
        Assert.Null(queries.Trace);
    }

    [Fact]
    public async Task A_branch_manager_without_a_home_branch_is_refused()
    {
        await Assert.ThrowsAsync<AuditLogAccessDeniedException>(() =>
            new AuditLogService(new RecordingQueries()).GetRequestsAsync(new AuditViewer(StaffRole.BranchManager, " "), new AuditLogFilter(Day, Day)));
    }

    [Theory]
    [InlineData(0, "The start date must be on or before the end date.")]
    [InlineData(366, "The period can be at most 366 days.")]
    public async Task A_period_that_is_reversed_or_longer_than_a_year_is_rejected_before_any_query(int days, string message)
    {
        var queries = new RecordingQueries();
        var filter = days == 0 ? new AuditLogFilter(Day, Day.AddDays(-1)) : new AuditLogFilter(Day.AddDays(-days), Day);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => new AuditLogService(queries).GetRequestsAsync(new AuditViewer(StaffRole.Auditor, null), filter));

        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
        Assert.Empty(queries.Filters);
    }

    [Fact]
    public async Task The_filter_is_normalized_blank_values_dropped_codes_upper_cased_and_the_page_kept_in_range()
    {
        var queries = new RecordingQueries();

        await new AuditLogService(queries).GetRequestsAsync(new AuditViewer(StaffRole.Auditor, null),
            new AuditLogFilter(Day.AddDays(-365), Day, BranchCode: " ", StaffId: " stf-clerk-01 ", Kind: " Account ", Result: "", CorrelationId: " corr ", Page: -3, PageSize: 10_000));

        var filter = Assert.Single(queries.Filters);
        Assert.Equal((null, "STF-CLERK-01", "Account", null, "corr", 1, AuditLogFilter.MaxPageSize),
            (filter.BranchCode, filter.StaffId, filter.Kind, filter.Result, filter.CorrelationId, filter.Page, filter.PageSize));
    }

    [Theory]
    [InlineData(0, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    [InlineData(101, 50, 3)]
    public void A_page_counts_its_last_partial_page_and_an_empty_result_still_has_one_page(int total, int pageSize, int pages)
    {
        Assert.Equal(pages, new AuditLogPage<int>([], 1, pageSize, total).TotalPages);
    }

    [Theory]
    [InlineData(StaffRole.Auditor, true, true, true)]
    [InlineData(StaffRole.Administrator, true, true, true)]
    [InlineData(StaffRole.BranchManager, true, false, false)]
    [InlineData(StaffRole.CounterClerk, false, false, false)]
    public void Each_role_has_the_documented_audit_log_access(StaffRole role, bool canView, bool allBranches, bool securityEvents)
    {
        var viewer = new AuditViewer(role, "BR-NORTH");

        Assert.Equal((canView, allBranches, securityEvents), (viewer.CanViewAuditLog, viewer.CanViewAllBranches, viewer.CanViewSecurityEvents));
    }

    private sealed class RecordingQueries : IAuditLogQueries
    {
        public List<AuditLogFilter> Filters { get; } = [];

        public (string CorrelationId, string? BranchCode, bool IncludeSecurityEvents, int Limit)? Trace { get; private set; }

        public IReadOnlyList<string> AuditedTables => ["Staff"];

        public Task<AuditLogPage<RequestAuditItem>> GetRequestsAsync(AuditLogFilter filter, CancellationToken cancellationToken = default) => Record<RequestAuditItem>(filter);

        public Task<AuditLogPage<DataChangeItem>> GetDataChangesAsync(AuditLogFilter filter, CancellationToken cancellationToken = default) => Record<DataChangeItem>(filter);

        public Task<AuditLogPage<SecurityEventItem>> GetSecurityEventsAsync(AuditLogFilter filter, CancellationToken cancellationToken = default) => Record<SecurityEventItem>(filter);

        public Task<AuditTrace> GetTraceAsync(string correlationId, string? branchCode, bool includeSecurityEvents, int limit, CancellationToken cancellationToken = default)
        {
            Trace = (correlationId, branchCode, includeSecurityEvents, limit);
            return Task.FromResult(new AuditTrace(correlationId, [], [], []));
        }

        private Task<AuditLogPage<T>> Record<T>(AuditLogFilter filter)
        {
            Filters.Add(filter);
            return Task.FromResult(new AuditLogPage<T>([], filter.Page, filter.PageSize, 0));
        }
    }
}
