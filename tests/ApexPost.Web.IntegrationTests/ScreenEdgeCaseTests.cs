using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApexPost.Web.IntegrationTests;

/// <summary>
/// The less travelled paths of the staff screens: public pages, the catalogue's stock JSON, money orders below the
/// verification threshold and a repeated verification, and the not-found and conflict answers of the admin screens.
/// </summary>
public sealed class ScreenEdgeCaseTests
{
    [Fact]
    public async Task The_public_pages_render_for_a_visitor_and_the_error_page_shows_only_a_request_id()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var visitor = factory.CreateClient();

        Assert.Contains("href=\"/Account/Login\">Sign in</a>", await visitor.GetStringAsync("/"), StringComparison.Ordinal);
        Assert.Contains("Privacy Policy", await visitor.GetStringAsync("/Home/Privacy"), StringComparison.Ordinal);
        var error = await visitor.GetAsync("/Home/Error");
        var page = await error.Content.ReadAsStringAsync();
        Assert.Contains("<strong>Request ID:</strong>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", page, StringComparison.Ordinal);
        Assert.Contains("no-store", error.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_stock_json_lists_the_clerks_own_branch_and_refuses_another_or_a_blank_branch()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var clerk = factory.CreateClient();
        await SignInAsync(clerk, "STF-CLERK-01");

        var own = await clerk.GetAsync("/Catalogue/StockJson?branchCode=br-north");
        using var json = JsonDocument.Parse(await own.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var item = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(("PRD-001", 100), (item.GetProperty("productId").GetString(), item.GetProperty("quantity").GetInt32()));
        Assert.StartsWith("/Account/AccessDenied", (await clerk.GetAsync("/Catalogue/StockJson?branchCode=BR-SOUTH")).Headers.Location?.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await clerk.GetAsync("/Catalogue/StockJson?branchCode=%20")).StatusCode);
    }

    [Fact]
    public async Task A_money_order_below_the_threshold_is_issued_straight_away_with_its_fee()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var clerk = factory.CreateClient();
        await SignInAsync(clerk, "STF-CLERK-01");
        var orderId = $"MO-LOW-{Guid.NewGuid():N}".ToUpperInvariant()[..20];

        var issued = await CreateMoneyOrderAsync(clerk, orderId, "100");

        Assert.Equal(HttpStatusCode.Redirect, issued.StatusCode);
        Assert.Equal("/MoneyOrders/Create", issued.Headers.Location?.OriginalString);
        // Flat 2.50 plus 1% of 100.
        Assert.Contains($"Money order {orderId} was created. Total: 103.50.", WebUtility.HtmlDecode(await clerk.GetStringAsync("/MoneyOrders/Create")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_verification_of_the_same_money_order_is_refused_and_the_number_is_never_echoed()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var clerk = factory.CreateClient();
        await SignInAsync(clerk, "STF-CLERK-01");
        var orderId = $"MO-TWICE-{Guid.NewGuid():N}".ToUpperInvariant()[..20];
        await CreateMoneyOrderAsync(clerk, orderId, "1500");
        Assert.Equal(HttpStatusCode.Redirect, (await VerifyAsync(clerk, orderId, "FIRST12345")).StatusCode);

        var again = await VerifyAsync(clerk, orderId, "SECOND6789");
        var page = await again.Content.ReadAsStringAsync();

        // A broken rule re-renders the page (only a data conflict is a 409): an already verified order shows its
        // verified status, the first verification is kept, and the audit marks the second attempt Failed.
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("Identity is verified.", page, StringComparison.Ordinal);
        Assert.Equal("****2345", (await factory.FindMoneyOrderAsync(orderId)).IdentityVerification?.MaskedDocumentReference.Value);
        Assert.Contains(await factory.GetAuditRecordsAsync(), record => record is { Controller: "MoneyOrders", Action: "Verify", StatusCode: 200, Outcome: "Failed" });
        Assert.DoesNotContain("SECOND6789", page, StringComparison.Ordinal);
        Assert.Contains("****2345", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await VerifyAsync(clerk, "MO-NEVER-ISSUED", "THIRD12345")).StatusCode);
    }

    [Fact]
    public async Task Branch_administration_answers_not_found_and_conflict_and_saves_an_edit()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var admin = factory.CreateClient();
        await SignInAsync(admin, "STF-ADMIN-01");
        var token = await TokenAsync(admin, "/BranchAdmin/Create");

        var duplicate = await admin.PostAsync("/BranchAdmin/Create", Form(token, ("BranchCode", "br-north"), ("Name", "Second North"), ("Region", "North"), ("CounterCount", "1"),
            ("WeekdaysOpen", "09:00"), ("WeekdaysClose", "17:00")));
        // The service finds the code taken before any insert, so the form comes back with the reason and the audit says Failed.
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("A branch with this code already exists.", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(await factory.GetAuditRecordsAsync(), record => record is { Controller: "BranchAdmin", Action: "Create", Outcome: "Failed" });

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/BranchAdmin/Edit/BR-NOWHERE")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/BranchAdmin/Edit", Form(token, ("BranchCode", "BR-NOWHERE"), ("Name", "Nowhere"), ("Region", "None"), ("WeekdaysOpen", "09:00"), ("WeekdaysClose", "17:00")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/BranchAdmin/Open/BR-NOWHERE", Form(token))).StatusCode);

        var edited = await admin.PostAsync("/BranchAdmin/Edit", Form(token, ("BranchCode", "BR-NORTH"), ("Name", "North Central"), ("Region", "North"), ("WeekdaysOpen", "09:00"), ("WeekdaysClose", "17:00")));
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Contains("North Central", await admin.GetStringAsync("/BranchAdmin"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_administrator_cannot_deactivate_their_own_account_and_an_unknown_staff_member_is_not_found()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var admin = factory.CreateClient();
        await SignInAsync(admin, "STF-ADMIN-01");
        var token = await TokenAsync(admin, "/StaffAdmin");

        var self = await admin.PostAsync("/StaffAdmin/Deactivate/STF-ADMIN-01", Form(token));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Contains("You cannot deactivate your own account.", await self.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // Still signed in and still active.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/StaffAdmin")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/StaffAdmin/Deactivate/STF-NOBODY", Form(token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/StaffAdmin/Edit/STF-NOBODY")).StatusCode);
    }

    private static FormUrlEncodedContent Form(string token, params (string Name, string Value)[] fields) =>
        new([new("__RequestVerificationToken", token), .. fields.Select(field => new KeyValuePair<string, string>(field.Name, field.Value))]);

    private static async Task<HttpResponseMessage> CreateMoneyOrderAsync(HttpClient client, string orderId, string amount)
    {
        var token = await TokenAsync(client, "/MoneyOrders/Create");
        return await client.PostAsync("/MoneyOrders/Create", Form(token, ("MoneyOrderId", orderId), ("BranchCode", "BR-NORTH"), ("Amount", amount),
            ("Sender", "Test Sender"), ("Recipient", "Test Recipient"), ("TransactionContextId", $"TX-{orderId}")));
    }

    private static async Task<HttpResponseMessage> VerifyAsync(HttpClient client, string orderId, string documentNumber)
    {
        var token = await TokenAsync(client, "/MoneyOrders/Create");
        return await client.PostAsync("/MoneyOrders/Verify", Form(token, ("MoneyOrderId", orderId), ("DocumentType", "Passport"), ("DocumentNumber", documentNumber)));
    }

    private static async Task SignInAsync(HttpClient client, string staffId)
    {
        var token = await TokenAsync(client, "/Account/Login");
        await client.PostAsync("/Account/Login", Form(token, ("StaffId", staffId), ("Password", AuthWebApplicationFactory.TestPassword)));
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var token = Regex.Match(await client.GetStringAsync(path), "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }
}
