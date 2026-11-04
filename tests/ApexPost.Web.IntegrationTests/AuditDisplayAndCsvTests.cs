using ApexPost.Application.Auditing;
using ApexPost.Web.Services;

namespace ApexPost.Web.IntegrationTests;

/// <summary>How the audit log shows and exports its values: one badge colour per meaning, and CSV a spreadsheet reads safely.</summary>
public sealed class AuditDisplayAndCsvTests
{
    [Theory]
    [InlineData(200, "text-bg-success")]
    [InlineData(302, "text-bg-success")]
    [InlineData(403, "text-bg-warning")]
    [InlineData(499, "text-bg-warning")]
    [InlineData(500, "text-bg-danger")]
    public void A_status_is_green_below_400_yellow_for_client_errors_and_red_for_server_errors(int status, string badge) =>
        Assert.Equal(badge, AuditDisplay.StatusBadge(status));

    [Theory]
    [InlineData(SecurityEventType.SignInSucceeded, "text-bg-success")]
    [InlineData(SecurityEventType.SignedOut, "text-bg-secondary")]
    [InlineData(SecurityEventType.SignInFailed, "text-bg-warning")]
    [InlineData(SecurityEventType.RateLimited, "text-bg-warning")]
    [InlineData(SecurityEventType.SessionRejected, "text-bg-warning")]
    [InlineData(SecurityEventType.AccessDenied, "text-bg-danger")]
    public void Each_security_event_has_the_badge_of_its_meaning(SecurityEventType eventType, string badge) =>
        Assert.Equal(badge, AuditDisplay.EventBadge(eventType));

    [Fact]
    public void Operations_outcomes_times_and_correlation_ids_are_shown_consistently()
    {
        Assert.Equal(["text-bg-success", "text-bg-warning", "text-bg-danger"], Enum.GetValues<DataChangeOperation>().Select(AuditDisplay.OperationBadge));
        Assert.Equal(("text-bg-success", "text-bg-danger"), (AuditDisplay.OutcomeBadge("Succeeded"), AuditDisplay.OutcomeBadge("Failed")));
        Assert.Equal("2026-11-04 09:05:07", AuditDisplay.Time(new DateTimeOffset(2026, 11, 4, 14, 35, 7, TimeSpan.FromHours(5.5))));
        Assert.Equal(("01234567…", "short-id", string.Empty), (AuditDisplay.ShortId("0123456789abcdef"), AuditDisplay.ShortId("short-id"), AuditDisplay.ShortId(null)));
    }

    [Fact]
    public void The_request_and_security_exports_have_their_headers_and_one_quoted_line_per_record()
    {
        var at = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var requests = AuditCsv.Requests([new RequestAuditItem(1, at, "STF-A", "Auditor", "BR-1", "AuditLog", "Export",
            new Dictionary<string, string> { ["id"] = "X" }, 200, 12, "corr-1", "Succeeded")]);
        var security = AuditCsv.SecurityEvents([new SecurityEventItem(2, at, SecurityEventType.AccessDenied, null, null, null, "GET", "/Reports", 302, "127.0.0.1", "Sent to the Access Denied page", "corr-2")]);

        Assert.Equal(
            "\"OccurredAtUtc\",\"StaffCode\",\"Role\",\"BranchCode\",\"Controller\",\"Action\",\"RouteValues\",\"StatusCode\",\"DurationMs\",\"Outcome\",\"CorrelationId\"\r\n" +
            "\"2026-11-04 09:00:00\",\"STF-A\",\"Auditor\",\"BR-1\",\"AuditLog\",\"Export\",\"id=X\",\"200\",\"12\",\"Succeeded\",\"corr-1\"\r\n",
            requests);
        var lines = security.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("\"OccurredAtUtc\",\"EventType\",\"StaffCode\"", lines[0], StringComparison.Ordinal);
        // An anonymous event leaves its staff, role and branch empty rather than writing "null".
        Assert.Equal("\"2026-11-04 09:00:00\",\"AccessDenied\",\"\",\"\",\"\",\"GET\",\"/Reports\",\"302\",\"Sent to the Access Denied page\",\"127.0.0.1\",\"corr-2\"", lines[1]);
    }
}
