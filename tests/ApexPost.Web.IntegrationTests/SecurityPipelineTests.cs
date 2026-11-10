using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ApexPost.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.Web.IntegrationTests;

public sealed class SecurityPipelineTests
{
    [Fact]
    public async Task Problem_details_does_not_expose_unexpected_exception_details()
    {
        var logger = new CapturingLogger<ProblemDetailsExceptionMiddleware>();
        var context = new DefaultHttpContext();
        context.Items[CorrelationIdMiddleware.ItemName] = "safe-correlation-1";
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        var middleware = new ProblemDetailsExceptionMiddleware(
            _ => throw new InvalidOperationException("synthetic-sensitive-detail"),
            logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        responseBody.Position = 0;
        var body = await new StreamReader(responseBody).ReadToEndAsync();
        Assert.DoesNotContain("synthetic-sensitive-detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stackTrace", body, StringComparison.OrdinalIgnoreCase);
        var logs = string.Join(Environment.NewLine, logger.Messages);
        Assert.Contains("safe-correlation-1", logs, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), logs, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-sensitive-detail", logs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aborted_request_cancellation_is_not_reported_as_server_error()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var context = new DefaultHttpContext();
        context.RequestAborted = cancellation.Token;
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        var middleware = new ProblemDetailsExceptionMiddleware(
            _ => Task.FromCanceled(cancellation.Token),
            NullLogger<ProblemDetailsExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, responseBody.Length);
    }

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
    public async Task The_content_security_policy_allows_no_inline_script_or_style_and_the_pages_need_none()
    {
        await using var factory = new SecurityWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Account/Login");
        var html = await response.Content.ReadAsStringAsync();

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self';", policy, StringComparison.Ordinal);
        Assert.Contains("style-src 'self';", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", policy, StringComparison.Ordinal);
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single(), StringComparison.Ordinal);

        // The pages must still work under that policy: every script is a file, and nothing is styled or scripted inline.
        // The rendered login page covers the shared layout; the scan covers every view's source.
        Assert.DoesNotMatch(InlineScriptOrStyle, html);
        var contentRoot = factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath;
        var views = Directory.GetFiles(Path.Combine(contentRoot, "Views"), "*.cshtml", SearchOption.AllDirectories);
        Assert.NotEmpty(views);
        Assert.All(views, view => Assert.DoesNotMatch(InlineScriptOrStyle, File.ReadAllText(view)));
    }

    private const string InlineScriptOrStyle = @"<script(?![^>]*\bsrc=)[^>]*>|<style\b|\sstyle=""|\son[a-z]+=""|javascript:";

    [Fact]
    public async Task Outside_development_the_sign_in_and_antiforgery_cookies_are_https_only()
    {
        await using var production = new SecurityWebApplicationFactory(environment: "Production");
        // Over HTTPS, as every request is once the HTTPS redirect has run: antiforgery refuses to issue an
        // HTTPS-only cookie on a plain HTTP request.
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var loginPage = await client.GetAsync("/Account/Login");

        var antiforgeryCookie = loginPage.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        Assert.Contains("secure", antiforgeryCookie, StringComparison.OrdinalIgnoreCase);
        var cookieOptions = production.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Equal(CookieSecurePolicy.Always, cookieOptions.Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, production.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>>().Value.Cookie.SecurePolicy);
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
    public async Task Integration_rate_limit_is_per_verified_partner_and_wrong_keys_do_not_spend_it()
    {
        await using var factory = new SecurityWebApplicationFactory(integrationPermitLimit: 60);
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(60, configuration.GetValue<int>("ApexPost:RateLimiting:IntegrationPermitLimit"));
        var failedKeyLimit = configuration.GetValue<int>("ApexPost:RateLimiting:IntegrationFailedKeyPermitLimit");
        Assert.InRange(failedKeyLimit, 1, 59);
        using var client = factory.CreateClient();

        // Wrong keys first, as many as their own budget allows: they are refused with 401 and spend none of the partner's.
        for (var attempt = 1; attempt <= failedKeyLimit; attempt++)
        {
            using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
            invalidRequest.Headers.Add("X-Partner-Id", $"invalid-partner-{attempt}");
            invalidRequest.Headers.Add("X-Api-Key", "invalid-key");
            using var invalidResponse = await client.SendAsync(invalidRequest);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        }

        // The partner's 60 requests all pass, whatever X-Partner-Id they claim: the partition is the verified key.
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
    public async Task Guessing_integration_keys_is_rate_limited_without_blocking_the_partner()
    {
        await using var factory = new SecurityWebApplicationFactory();
        var failedKeyLimit = factory.Services.GetRequiredService<IConfiguration>().GetValue<int>("ApexPost:RateLimiting:IntegrationFailedKeyPermitLimit");
        Assert.True(failedKeyLimit > 0);
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= failedKeyLimit; attempt++)
        {
            using var guess = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
            guess.Headers.Add("X-Api-Key", $"guess-{attempt}");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.SendAsync(guess)).StatusCode);
        }
        using var nextGuess = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
        nextGuess.Headers.Add("X-Api-Key", "guess-next");
        Assert.Equal((System.Net.HttpStatusCode)429, (await client.SendAsync(nextGuess)).StatusCode);
        // A missing key shares the guesser's budget.
        Assert.Equal((System.Net.HttpStatusCode)429, (await client.GetAsync("/integration/parcel-status?shipmentId=SHP-1")).StatusCode);

        using var partner = new HttpRequestMessage(HttpMethod.Get, "/integration/parcel-status?shipmentId=SHP-1");
        partner.Headers.Add("X-Api-Key", "test-integration-key");
        Assert.NotEqual((System.Net.HttpStatusCode)429, (await client.SendAsync(partner)).StatusCode);
    }

    [Fact]
    public void The_committed_login_limit_is_strict()
    {
        using var settings = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        var configuration = new ConfigurationBuilder().AddJsonStream(settings).Build();
        Assert.InRange(configuration.GetValue<int>("ApexPost:RateLimiting:LoginPermitLimit"), 1, 5);
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (exception is not null) message = $"{message}{Environment.NewLine}{exception}";
            Messages.Add(message);
        }
    }
}

public sealed class SecurityWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string PartnerOrigin = "https://tracking-widget.partner.example";
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "ApexPost-SecurityTests", Guid.NewGuid().ToString("N"));
    private readonly TestDatabase _database = new();
    private readonly bool _maintenance;
    private readonly string? _apiKey;
    private readonly string _allowedIp;
    private readonly int _integrationPermitLimit;
    private readonly string _environment;

    public SecurityWebApplicationFactory(bool maintenance = false, string? apiKey = "test-integration-key", string allowedIp = "127.0.0.1", int integrationPermitLimit = 60, string environment = "Development")
    {
        _maintenance = maintenance;
        _apiKey = apiKey;
        _allowedIp = allowedIp;
        _integrationPermitLimit = integrationPermitLimit;
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        // Program.cs reads these before Build(), which only UseSetting reaches.
        builder.UseSetting("ApexPost:DataDirectory", _dataDirectory);
        builder.UseSetting("ApexPost:RateLimiting:IntegrationPermitLimit", _integrationPermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _database.Attach(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApexPost:DemoSeed:Enabled"] = "false",
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

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DropAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }
}
