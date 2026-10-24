using System.Text.RegularExpressions;
using ApexPost.Application.Repositories;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ApexPost.Web.Middleware;
using ApexPost.Application.Auditing;

namespace ApexPost.Web.IntegrationTests;

public sealed class AuthenticationTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Unauthenticated_protected_route_redirects_to_login()
    {
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/CounterTransactions/New");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Active_staff_member_can_sign_in_with_test_only_configuration()
    {
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
    public async Task Branch_manager_is_authorized_to_branch_report()
    {
        await factory.SeedAsync();
        using var client = factory.CreateClient();
        await SignInAsync(client, "STF-MANAGER-01", AuthWebApplicationFactory.TestPassword);
        var response = await client.GetAsync("/Reports");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Audit_records_successful_authenticated_action_with_correlation_and_no_sensitive_input()
    {
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
    }

    [Fact]
    public async Task Performance_filter_warns_when_threshold_is_zero()
    {
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
    {
        var login = await client.GetStringAsync("/Account/Login");
        var token = Regex.Match(login, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }
}

public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string TestPassword = "test-only-password";
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "ApexPost-WebTests", Guid.NewGuid().ToString("N"));
    private readonly TestLoggerProvider _loggerProvider = new();

    public string DataDirectory => _dataDirectory;
    public IReadOnlyCollection<string> LogMessages => _loggerProvider.Messages;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApexPost:DataDirectory"] = _dataDirectory,
            ["ApexPost:Authentication:DemoAccounts:STF-CLERK-01"] = TestPassword,
            ["ApexPost:Authentication:DemoAccounts:STF-INACTIVE-01"] = TestPassword,
            ["ApexPost:Authentication:DemoAccounts:STF-MANAGER-01"] = TestPassword,
            ["ApexPost:Security:Integration:AllowedIps:0"] = "127.0.0.1",
            ["ApexPost:Security:Integration:AllowedIps:1"] = "::1",
            ["ApexPost:RateLimiting:LoginPermitLimit"] = "100"
            , ["ApexPost:Performance:WarningThresholdMilliseconds"] = "0"
        }));
        builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddProvider(_loggerProvider); });
        builder.ConfigureTestServices(services => services.PostConfigure<IntegrationSecurityOptions>(options => options.AllowedIps = ["127.0.0.1", "::1"]));
    }

    public new HttpClient CreateClient() => base.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffMemberRepository>();
        await staff.SaveAsync(new StaffMember(new StaffId("STF-CLERK-01"), "Test Clerk", StaffRole.CounterClerk, new BranchCode("BR-NORTH")));
        var inactive = new StaffMember(new StaffId("STF-INACTIVE-01"), "Inactive Clerk", StaffRole.CounterClerk, new BranchCode("BR-NORTH"));
        inactive.Deactivate();
        await staff.SaveAsync(inactive);
        await staff.SaveAsync(new StaffMember(new StaffId("STF-MANAGER-01"), "Test Manager", StaffRole.BranchManager, new BranchCode("BR-NORTH")));
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
