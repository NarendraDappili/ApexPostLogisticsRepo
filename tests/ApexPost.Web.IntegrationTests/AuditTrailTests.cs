using System.Text.Json;
using System.Text.RegularExpressions;
using ApexPost.Application.Auditing;
using ApexPost.Infrastructure.Auditing;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Web.IntegrationTests;

/// <summary>
/// The complete audit trail through the real pipeline: security events for sign-ins and refusals, the data history of
/// changes made from the screens, and the audit log lines. The request audit (AuditEntries) is covered in
/// <see cref="AuthenticationTests"/>; these tests also tie the three together by correlation ID.
/// </summary>
public sealed class AuditTrailTests
{
    [Fact]
    public async Task Sign_ins_and_sign_outs_are_security_events_with_the_reason_and_never_the_password()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();

        Assert.Equal(System.Net.HttpStatusCode.OK, (await SignInAsync(client, "STF-CLERK-01", "wrong-password-typed")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await SignInAsync(client, "a-password-in-the-id-field", AuthWebApplicationFactory.TestPassword)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await SignInAsync(client, "STF-INACTIVE-01", AuthWebApplicationFactory.TestPassword)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword)).StatusCode);
        var token = await GetAntiforgeryTokenAsync(client, "/Branch/Dashboard");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await client.PostAsync("/Account/Logout", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]))).StatusCode);

        var events = await SecurityEventsAsync(factory.Services);
        Assert.Equal(
            [("SignInFailed", "STF-CLERK-01", "WrongPassword", 200), ("SignInFailed", null, "UnknownStaff", 200), ("SignInFailed", "STF-INACTIVE-01", "Inactive", 200), ("SignInSucceeded", "STF-CLERK-01", null, 302), ("SignedOut", "STF-CLERK-01", null, 302)],
            events.Select(securityEvent => (securityEvent.EventType, securityEvent.StaffId, securityEvent.Detail, securityEvent.StatusCode)));
        var signedIn = events.Single(securityEvent => securityEvent.EventType == "SignInSucceeded");
        Assert.Equal(("CounterClerk", "BR-NORTH", "POST", "/Account/Login"), (signedIn.Role, signedIn.BranchCode, signedIn.HttpMethod, signedIn.Path));
        // Neither password, nor the "ID" that was really a password, is stored or logged anywhere.
        var stored = JsonSerializer.Serialize(events);
        Assert.All(new[] { "wrong-password-typed", AuthWebApplicationFactory.TestPassword, "A-PASSWORD-IN-THE-ID-FIELD" }, secret =>
        {
            Assert.DoesNotContain(secret, stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(factory.LogMessages, message => message.Contains(secret, StringComparison.OrdinalIgnoreCase));
        });
        // The sign-in's security event and its request audit share the request's correlation ID.
        Assert.Contains(await factory.GetAuditRecordsAsync(), record => record is { Controller: "Account", Action: "Login", StatusCode: 302 } && record.CorrelationId == signedIn.CorrelationId);
        Assert.Contains(factory.LogMessages, message => message.StartsWith("Audit security SignInFailed STF-CLERK-01 POST /Account/Login 200 WrongPassword", StringComparison.Ordinal));
        // The sign-in request itself runs before the cookie exists, so its request audit is anonymous; the
        // SignInSucceeded event above is what names the staff member.
        Assert.Contains(factory.LogMessages, message => message.StartsWith("Audit request Account/Login Succeeded 302 by anonymous", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refused_pages_policy_refusals_and_an_actions_own_branch_check_are_access_denied_events()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var clerk = factory.CreateClient();
        await SignInAsync(clerk, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await clerk.GetAsync("/Reports")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await clerk.PostAsync("/CounterTransactions/Void", new FormUrlEncodedContent([]))).StatusCode);
        // The action's own check: a clerk creating a shipment for another branch is refused with Forbid().
        var token = await GetAntiforgeryTokenAsync(clerk, "/Shipping/Create");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await clerk.PostAsync("/Shipping/Create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("ShipmentId", "SHP-OTHER-BRANCH"), new("TransactionId", "TX-OTHER-BRANCH"),
            new("OriginBranchCode", "BR-SOUTH"), new("DestinationZoneCode", "ZONE-01"), new("ShipmentType", "1"), new("ServiceLevel", "0"),
            new("WeightKilograms", "1"), new("LengthCentimetres", "10"), new("WidthCentimetres", "10"), new("HeightCentimetres", "10") ]))).StatusCode);

        var refusals = (await SecurityEventsAsync(factory.Services)).Where(securityEvent => securityEvent.EventType == "AccessDenied").ToArray();
        Assert.Equal(
            [("GET", "/Reports", 302), ("POST", "/CounterTransactions/Void", 403), ("POST", "/Shipping/Create", 403)],
            refusals.Select(refusal => (refusal.HttpMethod, refusal.Path, refusal.StatusCode)));
        Assert.All(refusals, refusal => Assert.Equal(("STF-CLERK-01", "CounterClerk", "BR-NORTH"), (refusal.StaffId, refusal.Role, refusal.BranchCode)));
        Assert.Contains(factory.LogMessages, message => message.StartsWith("Audit security AccessDenied STF-CLERK-01 GET /Reports 302", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_session_of_a_staff_member_who_was_deactivated_is_rejected_and_recorded_with_the_reason()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        await factory.DeactivateClerkAsync();

        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await client.GetAsync("/CounterTransactions/New")).StatusCode);

        var rejected = Assert.Single(await SecurityEventsAsync(factory.Services), securityEvent => securityEvent.EventType == "SessionRejected");
        Assert.Equal(("STF-CLERK-01", "Inactive", 401, "/CounterTransactions/New"), (rejected.StaffId, rejected.Detail, rejected.StatusCode, rejected.Path));
    }

    [Fact]
    public async Task An_administrators_staff_change_is_recorded_with_old_and_new_values_a_hidden_password_and_the_requests_correlation()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var admin = factory.CreateClient();
        await SignInAsync(admin, "STF-ADMIN-01", AuthWebApplicationFactory.TestPassword);
        var token = await GetAntiforgeryTokenAsync(admin, "/StaffAdmin/Edit/STF-CLERK-01");

        var response = await admin.PostAsync("/StaffAdmin/Edit/STF-CLERK-01", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("StaffId", "STF-CLERK-01"), new("Name", "Renamed Clerk"),
            new("Email", "renamed.clerk@example.test"), new("Role", "0"), new("BranchCode", "BR-NORTH"), new("Password", "a-brand-new-password") ]));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        var change = Assert.Single(await DataChangesAsync(factory.Services), row => row is { TableName: "Staff", Operation: "Update" });
        Assert.Equal(("StaffCode=STF-CLERK-01", "STF-ADMIN-01", "BR-NORTH"), (change.RecordKey, change.StaffId, change.BranchCode));
        var columns = DataChangeJson.Deserialize(change.ChangesJson).ToDictionary(column => column.Column);
        Assert.Equal(new DataChangeColumn("Name", "Test Clerk", "Renamed Clerk"), columns["Name"]);
        Assert.Equal(new DataChangeColumn("PasswordHash", DataChangeColumn.Hidden, DataChangeColumn.Hidden), columns["PasswordHash"]);
        Assert.DoesNotContain("a-brand-new-password", change.ChangesJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PBKDF2", change.ChangesJson, StringComparison.Ordinal);
        // The request audit, the data change and the audit log line all carry the same correlation ID.
        Assert.Contains(await factory.GetAuditRecordsAsync(), record => record is { Controller: "StaffAdmin", Action: "Edit", StatusCode: 302 } && record.CorrelationId == change.CorrelationId);
        var logLine = Assert.Single(factory.LogMessages, message => message.StartsWith("Audit data change Update Staff StaffCode=STF-CLERK-01", StringComparison.Ordinal));
        Assert.Contains("Name", logLine, StringComparison.Ordinal);
        Assert.EndsWith(change.CorrelationId!, logLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Renamed Clerk", logLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stock_adjustment_and_a_price_revision_from_the_screens_are_in_the_data_history()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var manager = factory.CreateClient();
        await SignInAsync(manager, "STF-MANAGER-01", AuthWebApplicationFactory.TestPassword);
        var managerToken = await GetAntiforgeryTokenAsync(manager, "/Catalogue");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await manager.PostAsync("/Catalogue/AdjustStock", new FormUrlEncodedContent([
            new("__RequestVerificationToken", managerToken), new("ProductId", "PRD-001"), new("Delta", "-5"), new("Reason", "Damaged in transit") ]))).StatusCode);

        using var admin = factory.CreateClient();
        await SignInAsync(admin, "STF-ADMIN-01", AuthWebApplicationFactory.TestPassword);
        var adminToken = await GetAntiforgeryTokenAsync(admin, "/Catalogue");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await admin.PostAsync("/Catalogue/RevisePrices", new FormUrlEncodedContent([
            new("__RequestVerificationToken", adminToken), new("CategoryCode", "GENERAL"), new("Percentage", "10") ]))).StatusCode);

        var changes = await DataChangesAsync(factory.Services);
        var stock = Assert.Single(changes, row => row.TableName == "BranchStock" && row.StaffId == "STF-MANAGER-01");
        Assert.Equal([new DataChangeColumn("Quantity", "100", "95")], DataChangeJson.Deserialize(stock.ChangesJson));
        Assert.Contains(changes, row => row is { TableName: "StockAdjustments", Operation: "Insert", StaffId: "STF-MANAGER-01" } && row.ChangesJson.Contains("Damaged in transit", StringComparison.Ordinal));
        var price = Assert.Single(changes, row => row is { TableName: "Products", RecordKey: "ProductCode=PRD-001" } && row.StaffId == "STF-ADMIN-01");
        Assert.Equal([new DataChangeColumn("UnitPrice", "10.00", "11.00")], DataChangeJson.Deserialize(price.ChangesJson));
    }

    [Fact]
    public async Task Rate_limited_sign_ins_and_rejected_partner_requests_are_security_events_without_the_key()
    {
        await using var factory = new SecurityWebApplicationFactory(apiKey: "test-integration-key");
        using var client = factory.CreateClient();
        var permitted = factory.Services.GetRequiredService<IConfiguration>().GetValue<int>("ApexPost:RateLimiting:LoginPermitLimit");
        for (var attempt = 0; attempt <= permitted; attempt++) await client.PostAsync("/Account/Login", new FormUrlEncodedContent([]));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/integration/parcel-status?shipmentId=SHP-1")).StatusCode);
        using var wrongKey = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
        wrongKey.Headers.Add("X-Api-Key", "a-guessed-partner-key");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.SendAsync(wrongKey)).StatusCode);

        var events = await SecurityEventsAsync(factory.Services);
        var limited = Assert.Single(events, securityEvent => securityEvent.EventType == "RateLimited");
        Assert.Equal(("POST", "/Account/Login", 429, "Rate limit 'login' exceeded"), (limited.HttpMethod, limited.Path, limited.StatusCode, limited.Detail));
        Assert.Equal(
            [("Missing partner API key", 401), ("Invalid partner API key", 401)],
            events.Where(securityEvent => securityEvent.EventType == "AccessDenied").Select(securityEvent => (securityEvent.Detail, securityEvent.StatusCode)));
        // The path is kept without its query string, and the supplied key is never stored.
        Assert.All(events, securityEvent => Assert.DoesNotContain("?", securityEvent.Path, StringComparison.Ordinal));
        Assert.DoesNotContain("a-guessed-partner-key", JsonSerializer.Serialize(events), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_partner_request_from_outside_the_ip_allowlist_is_an_access_denied_event()
    {
        await using var factory = new SecurityWebApplicationFactory(apiKey: "test-integration-key", allowedIp: "203.0.113.10");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
        request.Headers.Add("X-Api-Key", "test-integration-key");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);

        var refused = Assert.Single(await SecurityEventsAsync(factory.Services));
        Assert.Equal(("AccessDenied", 403, "Client IP is not in the allow-list", "/integration/parcel-status"), (refused.EventType, refused.StatusCode, refused.Detail, refused.Path));
    }

    private static async Task<List<SecurityEventEntity>> SecurityEventsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApexPostDbContext>().SecurityEvents.AsNoTracking().OrderBy(row => row.SecurityEventId).ToListAsync();
    }

    private static async Task<List<DataChangeEntity>> DataChangesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApexPostDbContext>().DataChanges.AsNoTracking().OrderBy(row => row.DataChangeId).ToListAsync();
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string staffId, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("StaffId", staffId), new("Password", password) ]));
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var page = await client.GetStringAsync(path);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }
}
