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
        }));
        builder.ConfigureLogging(logging => logging.ClearProviders());
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }
}
