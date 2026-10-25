using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ApexPost.Web.Middleware;

namespace ApexPost.Web.IntegrationTests;

public sealed class SecurityPipelineTests
{
    [Fact]
    public async Task Health_response_has_security_headers_and_reuses_valid_correlation_id()
    {
        await using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", "request-123");
        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("request-123", response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Maintenance_mode_returns_503_but_health_remains_available()
    {
        await using var factory = new SecurityWebApplicationFactory(maintenance: true);
        using var client = factory.CreateClient();
        var unavailable = await client.GetAsync("/");
        var health = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("120", unavailable.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Integration_requires_configured_api_key()
    {
        await using var unconfigured = new SecurityWebApplicationFactory(apiKey: null);
        using var unconfiguredClient = unconfigured.CreateClient();
        var configurationFailure = await unconfiguredClient.GetAsync("/integration/parcel-status?shipmentId=SHP-1");
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, configurationFailure.StatusCode);

        await using var configured = new SecurityWebApplicationFactory(apiKey: "test-integration-key");
        using var client = configured.CreateClient();
        var missing = await client.GetAsync("/integration/parcel-status?shipmentId=SHP-1");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, missing.StatusCode);
        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
        invalidRequest.Headers.Add("X-Api-Key", "wrong");
        var invalid = await client.SendAsync(invalidRequest);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task Integration_blocks_client_outside_allowlist()
    {
        await using var factory = new SecurityWebApplicationFactory(apiKey: "test-integration-key", allowedIp: "203.0.113.10");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
        request.Headers.Add("X-Api-Key", "test-integration-key");
        var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Integration_rate_limit_requires_api_key_and_is_shared_across_untrusted_partner_ids()
    {
        await using var factory = new SecurityWebApplicationFactory(integrationPermitLimit: 60);
        Assert.Equal(60, factory.Services.GetRequiredService<IConfiguration>().GetValue<int>("ApexPost:RateLimiting:IntegrationPermitLimit"));
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 60; attempt++)
        {
            using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
            invalidRequest.Headers.Add("X-Partner-Id", $"invalid-partner-{attempt}");
            invalidRequest.Headers.Add("X-Api-Key", "invalid-key");
            using var invalidResponse = await client.SendAsync(invalidRequest);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        }

        for (var attempt = 1; attempt <= 60; attempt++)
        {
            using var permittedRequest = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
            permittedRequest.Headers.Add("X-Partner-Id", $"partner-{attempt}");
            permittedRequest.Headers.Add("X-Api-Key", "test-integration-key");
            using var permittedResponse = await client.SendAsync(permittedRequest);
            Assert.NotEqual((System.Net.HttpStatusCode)429, permittedResponse.StatusCode);
        }

        using var rejectedRequest = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
        rejectedRequest.Headers.Add("X-Partner-Id", "partner-61");
        rejectedRequest.Headers.Add("X-Api-Key", "test-integration-key");
        using var rejectedResponse = await client.SendAsync(rejectedRequest);

        Assert.Equal((System.Net.HttpStatusCode)429, rejectedResponse.StatusCode);
    }

    [Fact]
    public async Task Login_rate_limit_returns_429()
    {
        await using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();
        var permittedRequests = factory.Services.GetRequiredService<IConfiguration>().GetValue<int>("ApexPost:RateLimiting:LoginPermitLimit");
        Assert.True(permittedRequests > 0);
        var permittedResponses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < permittedRequests; attempt++)
        {
            permittedResponses.Add(await client.PostAsync("/Account/Login", new FormUrlEncodedContent([])));
        }
        var rejected = await client.PostAsync("/Account/Login", new FormUrlEncodedContent([]));

        Assert.All(permittedResponses, response => Assert.NotEqual((System.Net.HttpStatusCode)429, response.StatusCode));
        Assert.Equal((System.Net.HttpStatusCode)429, rejected.StatusCode);
    }

    [Fact]
    public async Task Integration_cors_allows_only_configured_partner_origin()
    {
        await using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();
        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/integration/parcel-status");
        allowed.Headers.Add("Origin", SecurityWebApplicationFactory.PartnerOrigin);
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, allowedResponse.StatusCode);
        Assert.Equal(SecurityWebApplicationFactory.PartnerOrigin, allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var denied = new HttpRequestMessage(HttpMethod.Options, "/integration/parcel-status");
        denied.Headers.Add("Origin", "https://unapproved.example");
        denied.Headers.Add("Access-Control-Request-Method", "GET");
        var deniedResponse = await client.SendAsync(denied);
        Assert.False(deniedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }
}

public sealed class SecurityWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string PartnerOrigin = "https://tracking-widget.partner.example";
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "ApexPost-SecurityTests", Guid.NewGuid().ToString("N"));
    private readonly bool _maintenance;
    private readonly string? _apiKey;
    private readonly string _allowedIp;
    private readonly int _integrationPermitLimit;

    public SecurityWebApplicationFactory(bool maintenance = false, string? apiKey = "test-integration-key", string allowedIp = "127.0.0.1", int integrationPermitLimit = 60)
    {
        _maintenance = maintenance;
        _apiKey = apiKey;
        _allowedIp = allowedIp;
        _integrationPermitLimit = integrationPermitLimit;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApexPost:DataDirectory"] = _dataDirectory,
            ["ApexPost:Maintenance:Enabled"] = _maintenance.ToString(),
            ["ApexPost:Maintenance:RetryAfterSeconds"] = "120",
            ["ApexPost:Security:Integration:ApiKey"] = _apiKey,
            ["ApexPost:Security:Integration:AllowedIps:0"] = _allowedIp,
            ["ApexPost:Security:PartnerTrackingWidgetOrigin"] = PartnerOrigin,
            ["ApexPost:RateLimiting:IntegrationPermitLimit"] = _integrationPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<MaintenanceOptions>(options =>
            {
                options.Enabled = _maintenance;
                options.RetryAfterSeconds = 120;
            });
            services.PostConfigure<IntegrationSecurityOptions>(options =>
            {
                options.ApiKey = _apiKey;
                options.AllowedIps = [_allowedIp];
            });
        });
    }

    public new HttpClient CreateClient() => base.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }
}
