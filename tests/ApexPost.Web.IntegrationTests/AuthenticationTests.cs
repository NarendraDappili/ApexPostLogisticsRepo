using System.Text.RegularExpressions;
using ApexPost.Application.Pricing;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ApexPost.Web.Middleware;
using ApexPost.Application.Auditing;

namespace ApexPost.Web.IntegrationTests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Unauthenticated_protected_route_redirects_to_login()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/CounterTransactions/New");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Active_staff_member_can_sign_in_with_test_only_configuration()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var response = await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Branch/Dashboard", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), header => header.Contains("httponly", StringComparison.OrdinalIgnoreCase) && header.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Invalid_or_inactive_staff_cannot_sign_in()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var invalid = await SignInAsync(client, "STF-CLERK-01", "wrong-password");
        Assert.Equal(System.Net.HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Sign-in failed", await invalid.Content.ReadAsStringAsync());

        var inactive = await SignInAsync(client, "STF-INACTIVE-01", AuthWebApplicationFactory.TestPassword);
        Assert.Equal(System.Net.HttpStatusCode.OK, inactive.StatusCode);
        Assert.Contains("Sign-in failed", await inactive.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cookie_is_rejected_when_staff_becomes_inactive()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        await factory.DeactivateClerkAsync();
        var response = await client.GetAsync("/CounterTransactions/New");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Clerk_cannot_access_another_branch_or_privileged_report()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var otherBranch = await client.GetAsync("/Catalogue?branchCode=BR-SOUTH");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, otherBranch.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", otherBranch.Headers.Location?.AbsolutePath);

        var reports = await client.GetAsync("/Reports");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, reports.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", reports.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Workflow_screens_use_the_signed_in_clerks_branch_and_role_appropriate_navigation()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);

        var dashboard = await client.GetStringAsync("/Branch/Dashboard");
        var catalogue = await client.GetStringAsync("/Catalogue");
        var shipping = await client.GetStringAsync("/Shipping/Create");
        var moneyOrders = await client.GetStringAsync("/MoneyOrders/Create");

        Assert.Contains("value=\"BR-NORTH\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("Demo North Branch", dashboard, StringComparison.Ordinal);
        Assert.Contains("value=\"BR-NORTH\"", catalogue, StringComparison.Ordinal);
        Assert.Contains("Demo Retail Envelope", catalogue, StringComparison.Ordinal);
        Assert.Contains("value=\"BR-NORTH\"", shipping, StringComparison.Ordinal);
        Assert.Contains("value=\"BR-NORTH\"", moneyOrders, StringComparison.Ordinal);
        Assert.Contains("Counter basket", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain(">Reports</a>", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"ClerkId\"", await client.GetStringAsync("/CounterTransactions/New"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Branch_manager_cannot_load_reports_for_another_branch_by_direct_url()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-MANAGER-01", AuthWebApplicationFactory.TestPassword);

        var response = await client.GetAsync("/Reports?branchCode=BR-SOUTH");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Checkout_post_redirects_and_reduces_stock()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var token = await GetAntiforgeryTokenAsync(client, "/CounterTransactions/New");
        var transactionId = $"TX-WEB-{Guid.NewGuid():N}";

        var response = await client.PostAsync("/CounterTransactions/New", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token),
            new("TransactionId", transactionId),
            new("BranchCode", "BR-NORTH"),
            new("CounterCode", "C-01"),
            new("PaymentMethod", "0"),
            new("ProductId", "PRD-001"),
            new("RetailQuantity", "2"),
            new("ServiceQuantity", "1"),
            new("MoneyOrderIds", "")
        ]));

        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        Assert.Equal("/CounterTransactions/New", response.Headers.Location?.OriginalString);
        var confirmation = await client.GetStringAsync(response.Headers.Location
            ?? throw new InvalidOperationException("Successful checkout did not return a redirect location."));
        Assert.True(confirmation.Contains($"Transaction {transactionId.ToUpperInvariant()} was paid.", StringComparison.Ordinal), confirmation);
        Assert.Equal(98, await factory.GetNorthBranchStockAsync());
    }

    [Fact]
    public async Task Checkout_rejects_a_post_without_antiforgery_token()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);

        var response = await client.PostAsync("/CounterTransactions/New", new FormUrlEncodedContent([
            new("TransactionId", "TX-WEB-002"),
            new("BranchCode", "BR-NORTH"),
            new("CounterCode", "C-01"),
            new("PaymentMethod", "0"),
            new("ProductId", "PRD-001"),
            new("RetailQuantity", "1")
        ]));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100, await factory.GetNorthBranchStockAsync());
    }

    [Fact]
    public async Task Shipping_weight_limit_and_high_value_money_order_errors_are_shown_without_posting()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var shippingToken = await GetAntiforgeryTokenAsync(client, "/Shipping/Create");
        var shippingResponse = await client.PostAsync("/Shipping/Create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", shippingToken),
            new("ShipmentId", "SHP-WEB-TOO-HEAVY"),
            new("OriginBranchCode", "BR-NORTH"),
            new("DestinationZoneCode", "ZONE-01"),
            new("ShipmentType", "1"),
            new("ServiceLevel", "0"),
            new("WeightKilograms", "6"),
            new("LengthCentimetres", "10"),
            new("WidthCentimetres", "10"),
            new("HeightCentimetres", "10")
        ]));
        var shippingPage = await shippingResponse.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, shippingResponse.StatusCode);
        Assert.True(shippingPage.Contains("configured service-level weight limit", StringComparison.OrdinalIgnoreCase), shippingPage);

        var moneyOrderToken = await GetAntiforgeryTokenAsync(client, "/MoneyOrders/Create");
        var moneyOrderResponse = await client.PostAsync("/MoneyOrders/Create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", moneyOrderToken),
            new("MoneyOrderId", "MO-WEB-VERIFY"),
            new("BranchCode", "BR-NORTH"),
            new("Amount", "1500"),
            new("Sender", "Synthetic Sender"),
            new("Recipient", "Synthetic Recipient"),
            new("Fee", "5"),
            new("TransactionContextId", "TX-WEB-VERIFY"),
            new("VerificationOutcome", "0")
        ]));
        var moneyOrderPage = await moneyOrderResponse.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.OK, moneyOrderResponse.StatusCode);
        Assert.Contains("successful verification", moneyOrderPage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FULL-DOCUMENT", moneyOrderPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eligible_shipment_post_redirects_to_label_with_configured_charge()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var token = await GetAntiforgeryTokenAsync(client, "/Shipping/Create");
        var shipmentId = $"SHP-WEB-{Guid.NewGuid():N}";

        var response = await client.PostAsync("/Shipping/Create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token),
            new("ShipmentId", shipmentId),
            new("OriginBranchCode", "BR-NORTH"),
            new("DestinationZoneCode", "ZONE-01"),
            new("ShipmentType", "1"),
            new("ServiceLevel", "0"),
            new("WeightKilograms", "4"),
            new("LengthCentimetres", "10"),
            new("WidthCentimetres", "10"),
            new("HeightCentimetres", "10")
        ]));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Shipping/Label/", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        var label = await client.GetStringAsync(response.Headers.Location
            ?? throw new InvalidOperationException("Successful shipment did not redirect to its label."));
        Assert.Contains("12.00", label, StringComparison.Ordinal);
        Assert.Contains(shipmentId.ToUpperInvariant(), label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verified_high_value_money_order_post_redirects_and_persists_only_masked_document_data()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        var token = await GetAntiforgeryTokenAsync(client, "/MoneyOrders/Create");
        var orderId = $"MO-WEB-{Guid.NewGuid():N}";

        var response = await client.PostAsync("/MoneyOrders/Create", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token),
            new("MoneyOrderId", orderId),
            new("BranchCode", "BR-NORTH"),
            new("Amount", "1500"),
            new("Sender", "Synthetic Sender"),
            new("Recipient", "Synthetic Recipient"),
            new("Fee", "5"),
            new("TransactionContextId", "TX-WEB-VERIFIED"),
            new("DocumentType", "Synthetic ID"),
            new("DocumentNumber", "SYNTHETIC-1234"),
            new("VerificationOutcome", "1")
        ]));

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/MoneyOrders/Create", response.Headers.Location?.OriginalString);
        var confirmation = await client.GetStringAsync("/MoneyOrders/Create");
        Assert.Contains($"Money order {orderId.ToUpperInvariant()} was created.", confirmation, StringComparison.Ordinal);
        Assert.DoesNotContain("SYNTHETIC-1234", confirmation, StringComparison.Ordinal);
        var saved = await factory.FindMoneyOrderAsync(orderId);
        Assert.Equal("****1234", saved.IdentityVerification?.MaskedDocumentReference.Value);
    }

    [Fact]
    public async Task Reports_show_empty_states_for_a_branch_without_activity()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedEmptyReportBranchAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-EMPTY-MANAGER", AuthWebApplicationFactory.TestPassword);

        var response = await client.GetAsync("/Reports?day=2020-01-01");
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Daily sales: 2020-01-01", page, StringComparison.Ordinal);
        Assert.Contains("No paid transactions were recorded", page, StringComparison.Ordinal);
        Assert.Contains("No products are at or below the low-stock threshold", page, StringComparison.Ordinal);
        Assert.Contains("No shipments are recorded for this branch", page, StringComparison.Ordinal);
        Assert.Contains("No money orders are recorded for this branch", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clerk_receives_forbidden_response_for_void_endpoint()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);

        var response = await client.PostAsync("/CounterTransactions/Void", new FormUrlEncodedContent([]));
        var refund = await client.PostAsync("/CounterTransactions/Refund", new FormUrlEncodedContent([]));

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, refund.StatusCode);
    }

    [Fact]
    public async Task Branch_manager_is_authorized_to_branch_report()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-MANAGER-01", AuthWebApplicationFactory.TestPassword);
        var response = await client.GetAsync("/Reports");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Audit_records_successful_authenticated_action_with_correlation_and_no_sensitive_input()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/CounterTransactions/New");
        request.Headers.Add("X-Correlation-Id", "audit-success-1");
        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var record = (await factory.GetAuditRecordsAsync()).Last(entry => entry.CorrelationId == "audit-success-1");
        Assert.Equal("STF-CLERK-01", record.StaffId);
        Assert.Equal("CounterClerk", record.Role);
        Assert.Equal("BR-NORTH", record.BranchCode);
        Assert.Equal("CounterTransactions", record.Controller);
        Assert.Equal("New", record.Action);
        Assert.Equal(200, record.StatusCode);
        Assert.Equal("Succeeded", record.Outcome);
        Assert.DoesNotContain("password", record.RouteValues.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("document", record.RouteValues.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_model_returns_validation_view_and_is_audited_without_password()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = await GetAntiforgeryTokenAsync(client);
        var response = await client.PostAsync("/Account/Login?documentNumber=FULL-DOCUMENT-1234&apiKey=not-an-audit-key", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("StaffId", ""), new("Password", "do-not-audit-this-password") ]));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("required", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var record = (await factory.GetAuditRecordsAsync()).Last(entry => entry.Controller == "Account" && entry.Action == "Login");
        Assert.Equal("Failed", record.Outcome);
        var serializedRecord = System.Text.Json.JsonSerializer.Serialize(record);
        Assert.DoesNotContain("do-not-audit-this-password", serializedRecord, StringComparison.Ordinal);
        Assert.DoesNotContain("FULL-DOCUMENT-1234", serializedRecord, StringComparison.Ordinal);
        Assert.DoesNotContain("not-an-audit-key", serializedRecord, StringComparison.Ordinal);
        var logs = string.Join(Environment.NewLine, factory.LogMessages);
        Assert.DoesNotContain("do-not-audit-this-password", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("FULL-DOCUMENT-1234", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("not-an-audit-key", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Performance_filter_warns_when_threshold_is_zero()
    {
        await using var factory = new AuthWebApplicationFactory();
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-CLERK-01", AuthWebApplicationFactory.TestPassword);
        await client.GetAsync("/CounterTransactions/New");

        Assert.Contains(factory.LogMessages, message => message.Contains("MVC action CounterTransactions/New took", StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string staffId, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent([
            new("__RequestVerificationToken", token), new("StaffId", staffId), new("Password", password) ]));
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        => await GetAntiforgeryTokenAsync(client, "/Account/Login");

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var page = await client.GetStringAsync(path);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }
}

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string TestPassword = "test-only-password";
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "ApexPost-WebTests", Guid.NewGuid().ToString("N"));
    private readonly TestLoggerProvider _loggerProvider = new();

    public IReadOnlyCollection<string> LogMessages => _loggerProvider.Messages;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApexPost:DataDirectory"] = _dataDirectory,
            ["ApexPost:Authentication:DemoAccounts:STF-CLERK-01"] = TestPassword,
            ["ApexPost:Authentication:DemoAccounts:STF-INACTIVE-01"] = TestPassword,
            ["ApexPost:Authentication:DemoAccounts:STF-MANAGER-01"] = TestPassword,
            ["ApexPost:Authentication:DemoAccounts:STF-EMPTY-MANAGER"] = TestPassword,
            ["ApexPost:Security:Integration:AllowedIps:0"] = "127.0.0.1",
            ["ApexPost:Security:Integration:AllowedIps:1"] = "::1",
            ["ApexPost:RateLimiting:LoginPermitLimit"] = "100",
            ["ApexPost:Performance:WarningThresholdMilliseconds"] = "0"
        }));
        builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddProvider(_loggerProvider); });
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<IntegrationSecurityOptions>(options => options.AllowedIps = ["127.0.0.1", "::1"]);
            services.RemoveAll<IShippingRateProvider>();
            services.AddSingleton<IShippingRateProvider>(new TestShippingRateProvider());
        });
    }

    public new HttpClient CreateClient() => base.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var branches = scope.ServiceProvider.GetRequiredService<IBranchRepository>();
        var counters = scope.ServiceProvider.GetRequiredService<ICounterRepository>();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>();
        var products = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        await branches.SaveAsync(new Branch(new BranchCode("BR-NORTH"), "Demo North Branch"));
        await counters.SaveAsync(new Counter(new CounterCode("C-01"), new BranchCode("BR-NORTH")));
        await staff.SaveAsync(new StaffMember(new StaffId("STF-CLERK-01"), "Test Clerk", StaffRole.CounterClerk, new BranchCode("BR-NORTH")));
        var inactive = new StaffMember(new StaffId("STF-INACTIVE-01"), "Inactive Clerk", StaffRole.CounterClerk, new BranchCode("BR-NORTH"));
        inactive.Deactivate();
        await staff.SaveAsync(inactive);
        await staff.SaveAsync(new StaffMember(new StaffId("STF-MANAGER-01"), "Test Manager", StaffRole.BranchManager, new BranchCode("BR-NORTH")));
        await products.SaveAsync(new Product(new ProductId("PRD-001"), "Demo Retail Envelope", new Money(10m)));
        await inventory.SaveAsync(new BranchInventoryItem(new BranchCode("BR-NORTH"), new ProductId("PRD-001"), 100));
    }

    public async Task SeedEmptyReportBranchAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBranchRepository>()
            .SaveAsync(new Branch(new BranchCode("BR-EMPTY"), "Empty Report Branch"));
        await scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>()
            .SaveAsync(new StaffMember(new StaffId("STF-EMPTY-MANAGER"), "Empty Report Manager", StaffRole.BranchManager, new BranchCode("BR-EMPTY")));
    }

    public async Task<int> GetNorthBranchStockAsync()
    {
        using var scope = Services.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryRepository>();
        return (await inventory.FindAsync(new BranchCode("BR-NORTH"), new ProductId("PRD-001")))?.Quantity
            ?? throw new InvalidOperationException("North-branch test stock was not seeded.");
    }

    public async Task<MoneyOrder> FindMoneyOrderAsync(string id)
    {
        using var scope = Services.CreateScope();
        var moneyOrders = scope.ServiceProvider.GetRequiredService<IMoneyOrderRepository>();
        return await moneyOrders.FindByIdAsync(new MoneyOrderId(id))
            ?? throw new InvalidOperationException("The test money order was not persisted.");
    }

    public async Task DeactivateClerkAsync()
    {
        using var scope = Services.CreateScope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>();
        var clerk = await staff.FindByIdAsync(new StaffId("STF-CLERK-01")) ?? throw new InvalidOperationException("Test clerk was not seeded.");
        clerk.Deactivate();
        await staff.SaveAsync(clerk);
    }

    public async Task<IReadOnlyList<AuditRecord>> GetAuditRecordsAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAuditStore>().ReadAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    private sealed class TestShippingRateProvider : IShippingRateProvider
    {
        public Task<IReadOnlyCollection<ShippingRate>> GetRatesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<ShippingRate>>([
                new ShippingRate(ShipmentType.Parcel, ShippingServiceLevel.Standard, new ZoneCode("ZONE-01"), new Money(12m), new ParcelWeight(5m))
            ]);
    }
}

public sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();
    public IReadOnlyCollection<string> Messages => _messages.ToArray();
    public ILogger CreateLogger(string categoryName) => new TestLogger(_messages);
    public void Dispose() { }

    private sealed class TestLogger(System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
