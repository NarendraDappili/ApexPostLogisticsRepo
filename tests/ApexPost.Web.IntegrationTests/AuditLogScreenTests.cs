using System.Net;
using System.Text.RegularExpressions;
using ApexPost.Application.Auditing;
using ApexPost.Domain.Enums;
using ApexPost.Web.Services;

namespace ApexPost.Web.IntegrationTests;

/// <summary>
/// The audit log screens for each role through the real pipeline: who sees the menu entry and which lists, what a
/// Branch Manager is kept to, the trace view, the CSV export and the page's theme rules (strict CSP, no inline styles).
/// </summary>
public sealed class AuditLogScreenTests
{
    [Fact]
    public async Task A_counter_clerk_has_no_audit_menu_entry_and_is_refused_every_audit_page()
    {
        await using var factory = await FactoryAsync();
        using var clerk = factory.CreateClient();
        await SignInAsync(clerk, "STF-CLERK-01");

        Assert.DoesNotContain(">Audit log</a>", await clerk.GetStringAsync("/Branch/Dashboard"), StringComparison.Ordinal);
        foreach (var path in new[] { "/AuditLog", "/AuditLog/Requests", "/AuditLog/Changes", "/AuditLog/Security", "/AuditLog/Trace/corr-1", "/AuditLog/Export?tab=Requests" })
        {
            var response = await clerk.GetAsync(path);
            // /AuditLog itself redirects to the list, which is then refused.
            Assert.True(response.StatusCode == HttpStatusCode.Redirect, path);
            Assert.StartsWith("/Account/AccessDenied", response.Headers.Location?.AbsolutePath ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_branch_manager_reads_their_branchs_requests_and_changes_but_not_security_events_or_another_branch()
    {
        await using var factory = await FactoryAsync();
        using var manager = factory.CreateClient();
        await SignInAsync(manager, "STF-MANAGER-01");
        await manager.GetAsync("/Branch/Dashboard");

        var page = await manager.GetStringAsync("/AuditLog/Requests");
        Assert.Contains("class=\"nav-link\" aria-current=\"page\" href=\"/AuditLog/Requests\"", page, StringComparison.Ordinal);
        Assert.Contains(">Data changes</a>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Security events</a>", page, StringComparison.Ordinal);
        // The branch is fixed to the manager's home branch, shown as text, not offered as a choice.
        Assert.Contains("type=\"hidden\" id=\"branchCode\" name=\"branchCode\" value=\"BR-NORTH\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("All branches</option>", page, StringComparison.Ordinal);
        Assert.Contains("Branch / Dashboard", page, StringComparison.Ordinal);
        // A sign-in request is anonymous and has no branch yet, so only Auditors and Administrators see it.
        Assert.DoesNotContain("Account / Login", page, StringComparison.Ordinal);

        Assert.StartsWith("/Account/AccessDenied", (await manager.GetAsync("/AuditLog/Security")).Headers.Location?.AbsolutePath, StringComparison.Ordinal);
        Assert.StartsWith("/Account/AccessDenied", (await manager.GetAsync("/AuditLog/Requests?branchCode=BR-SOUTH")).Headers.Location?.AbsolutePath, StringComparison.Ordinal);
        Assert.StartsWith("/Account/AccessDenied", (await manager.GetAsync("/AuditLog/Export?tab=Security")).Headers.Location?.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_auditor_reads_every_list_for_every_branch_including_failed_sign_ins()
    {
        await using var factory = await FactoryAsync();
        using var visitor = factory.CreateClient();
        await SignInAsync(visitor, "STF-CLERK-01", "not-the-password");
        using var auditor = factory.CreateClient();
        await SignInAsync(auditor, "STF-AUDITOR-01");

        var dashboard = await auditor.GetStringAsync("/Branch/Dashboard");
        Assert.Contains(">Audit log</a>", dashboard, StringComparison.Ordinal);
        var security = await auditor.GetStringAsync("/AuditLog/Security?kind=SignInFailed");
        Assert.Contains("aria-current=\"page\" href=\"/AuditLog/Security", security, StringComparison.Ordinal);
        Assert.Contains("<option value=\"\">All branches</option>", security, StringComparison.Ordinal);
        Assert.Contains("Sign In Failed</span>", security, StringComparison.Ordinal);
        Assert.Contains("WrongPassword", security, StringComparison.Ordinal);
        Assert.Contains("badge text-bg-warning", security, StringComparison.Ordinal);
        Assert.DoesNotContain("not-the-password", security, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await auditor.GetAsync("/AuditLog/Changes?branchCode=BR-SOUTH")).StatusCode);
    }

    [Fact]
    public async Task An_administrators_change_shows_its_old_and_new_values_and_its_trace_ties_the_request_to_the_change()
    {
        await using var factory = await FactoryAsync();
        using var admin = factory.CreateClient();
        await SignInAsync(admin, "STF-ADMIN-01");
        var token = await TokenAsync(admin, "/StaffAdmin/Edit/STF-CLERK-01");
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/StaffAdmin/Edit/STF-CLERK-01", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("StaffId", "STF-CLERK-01"), new("Name", "Renamed Clerk"),
            new("Email", "renamed.clerk@example.test"), new("Role", "0"), new("BranchCode", "BR-NORTH") ]))).StatusCode);

        var changes = await admin.GetStringAsync("/AuditLog/Changes?kind=Staff&result=Update");
        Assert.Contains("<del class=\"audit-old\"><span class=\"visually-hidden\">was </span>Test Clerk</del>", changes, StringComparison.Ordinal);
        Assert.Contains("<ins class=\"audit-new\"><span class=\"visually-hidden\">now </span>Renamed Clerk</ins>", changes, StringComparison.Ordinal);
        Assert.Contains("StaffCode=STF-CLERK-01", changes, StringComparison.Ordinal);
        Assert.Contains("<option value=\"Staff\" selected=\"selected\">Staff</option>", changes, StringComparison.Ordinal);

        var traceLink = Regex.Match(changes, "href=\"(/AuditLog/Trace/[^\"]+)\"").Groups[1].Value;
        var trace = await admin.GetStringAsync(traceLink);
        Assert.Contains("StaffAdmin / Edit", trace, StringComparison.Ordinal);
        Assert.Contains("Renamed Clerk", trace, StringComparison.Ordinal);
        Assert.Contains("This request raised no security event.", trace, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_csv_export_has_a_header_the_filtered_rows_and_neutralizes_spreadsheet_formulas()
    {
        await using var factory = await FactoryAsync();
        using var admin = factory.CreateClient();
        await SignInAsync(admin, "STF-ADMIN-01");
        var token = await TokenAsync(admin, "/StaffAdmin/Edit/STF-CLERK-01");
        await admin.PostAsync("/StaffAdmin/Edit/STF-CLERK-01", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("StaffId", "STF-CLERK-01"), new("Name", "=HYPERLINK(\"http://evil.example\")"),
            new("Email", "clerk@example.test"), new("Role", "0"), new("BranchCode", "BR-NORTH") ]));

        var response = await admin.GetAsync("/AuditLog/Export?tab=Changes&kind=Staff");
        var csv = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("attachment", response.Content.Headers.ContentDisposition?.DispositionType, StringComparison.Ordinal);
        Assert.StartsWith("\"OccurredAtUtc\",\"StaffCode\",\"BranchCode\",\"TableName\",\"RecordKey\",\"Operation\",\"Changes\",\"CorrelationId\"", csv.TrimStart('﻿'), StringComparison.Ordinal);
        Assert.Contains("\"Staff\",\"StaffCode=STF-CLERK-01\",\"Update\"", csv, StringComparison.Ordinal);
        // The change text starts with the column name, so the formula is inside the field, and quotes are doubled.
        Assert.Contains("Name: Test Clerk -> =HYPERLINK(\"\"http://evil.example\"\")", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void A_csv_field_a_spreadsheet_would_run_as_a_formula_is_prefixed_with_an_apostrophe()
    {
        var csv = AuditCsv.Changes([new DataChangeItem(1, DateTimeOffset.UnixEpoch, "STF-A", "BR-1", "Staff", "=1+1", DataChangeOperation.Update, [], "-cmd")]);

        Assert.Contains("\"'=1+1\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"'-cmd\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reversed_period_or_an_unreadable_date_is_a_400_with_the_form_and_a_reason()
    {
        await using var factory = await FactoryAsync();
        using var auditor = factory.CreateClient();
        await SignInAsync(auditor, "STF-AUDITOR-01");

        var reversed = await auditor.GetAsync("/AuditLog/Requests?from=2026-11-04&to=2026-11-01");
        var unreadable = await auditor.GetAsync("/AuditLog/Changes?from=not-a-date");

        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Contains("The start date must be on or before the end date.", await reversed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unreadable.StatusCode);
        Assert.Contains("Check the filter", await unreadable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await auditor.GetAsync($"/AuditLog/Trace/{new string('x', 65)}")).StatusCode);
    }

    [Fact]
    public async Task Long_lists_are_paged_in_the_brand_colours_and_the_pages_follow_the_strict_csp()
    {
        await using var factory = await FactoryAsync();
        using var auditor = factory.CreateClient();
        await SignInAsync(auditor, "STF-AUDITOR-01");
        for (var visit = 0; visit < AuditLogFilter.DefaultPageSize + 2; visit++) await auditor.GetAsync("/Branch/Dashboard");

        var first = await auditor.GetStringAsync("/AuditLog/Requests?staffId=stf-auditor-01");
        var second = await auditor.GetStringAsync("/AuditLog/Requests?staffId=STF-AUDITOR-01&page=2");

        Assert.Contains("<ul class=\"pagination", first, StringComparison.Ordinal);
        Assert.Contains("Showing <strong>1&ndash;25</strong>", first, StringComparison.Ordinal);
        Assert.Contains("class=\"page-item active\"><a class=\"page-link\" aria-current=\"page\"", second, StringComparison.Ordinal);
        Assert.Contains("Showing <strong>26&ndash;", second, StringComparison.Ordinal);
        // The strict CSP (style-src 'self') would block inline styles; the screens use only site.css classes.
        Assert.All(new[] { first, second }, page =>
        {
            Assert.DoesNotContain(" style=\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("<style", page, StringComparison.Ordinal);
            Assert.Contains("class=\"card audit-filter mb-3\"", page, StringComparison.Ordinal);
        });
        Assert.Contains(".audit-tabs .nav-link[aria-current=\"page\"]", await auditor.GetStringAsync("/css/site.css"), StringComparison.Ordinal);
    }

    private static async Task<AuthWebApplicationFactory> FactoryAsync()
    {
        var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        await factory.SeedSouthBranchAsync();
        await factory.SeedStaffAsync("STF-AUDITOR-01", StaffRole.Auditor, "BR-NORTH");
        return factory;
    }

    private static async Task SignInAsync(HttpClient client, string staffId, string password = AuthWebApplicationFactory.TestPassword)
    {
        var token = await TokenAsync(client, "/Account/Login");
        await client.PostAsync("/Account/Login", new FormUrlEncodedContent([new("__RequestVerificationToken", token), new("StaffId", staffId), new("Password", password)]));
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var token = Regex.Match(await client.GetStringAsync(path), "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }
}
