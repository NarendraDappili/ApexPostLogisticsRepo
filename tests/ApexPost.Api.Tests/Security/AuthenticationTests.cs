using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using ApexPost.Api.Security;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Tests.Security;

/// <summary>
/// D1: JWT bearer authentication. Every API endpoint needs a token whose issuer, audience, lifetime and signature
/// check out; anything else is a 401 ProblemDetails with an RFC 6750 WWW-Authenticate header. The probe, the OpenAPI
/// documents and Swagger UI stay open. The API refuses to start without the token settings. None of this reaches the
/// database: a request without a valid token is turned away before any action runs.
/// </summary>
public sealed class AuthenticationTests
{
    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/v2/shipments" },
        { "GET", "/api/v1/shipments/APX-24000001" },
        { "POST", "/api/v2/shipments" },
        { "PATCH", "/api/v2/shipments/APX-24000001/status" },
        { "DELETE", "/api/v1/shipments/APX-24000001" },
        { "GET", "/api/v2/shipments/APX-24000001/scans" },
        { "POST", "/api/v2/shipments/APX-24000001/scans" },
        { "GET", "/api/shipments" },
        // Without a token even a path that does not exist is a 401, so the answer reveals no routes.
        { "GET", "/api/v2/does-not-exist" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Without_a_token_every_endpoint_is_a_401_problem(string method, string path)
    {
        using var response = await SendAsync(method, path, token: null);
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
        Assert.Equal(("Authentication is required.", 401), (problem.GetProperty("title").GetString(), problem.GetProperty("status").GetInt32()));
        Assert.Contains("Authorization: Bearer", problem.GetProperty("detail").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task An_expired_token_is_a_401_that_says_it_expired()
    {
        var expired = TestTokens.Create([ApiRoles.Admin], expires: DateTime.UtcNow.AddMinutes(-2));

        using var response = await SendAsync("GET", "/api/v2/shipments", expired);
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer error=\"invalid_token\", error_description=\"The token expired\"", Assert.Single(response.Headers.WwwAuthenticate).ToString());
        Assert.Contains("expired", problem.GetProperty("detail").GetString());
    }

    public static TheoryData<string, string> InvalidTokens => new()
    {
        { "signed with another key", TestTokens.Create([ApiRoles.Admin], signingKey: RandomNumberGenerator.GetBytes(32)) },
        { "from another issuer", TestTokens.Create([ApiRoles.Admin], issuer: "https://login.example") },
        { "for another audience", TestTokens.Create([ApiRoles.Admin], audience: "https://other-api.example") },
        { "not signed at all", Unsigned() },
        { "without an expiry", WithoutExpiry() },
        { "not a JWT", "not-a-token" },
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task A_token_that_does_not_check_out_is_a_401(string why, string token)
    {
        using var response = await SendAsync("GET", "/api/v2/shipments", token);
        var problem = await response.JsonAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, why);
        Assert.Equal("Bearer error=\"invalid_token\", error_description=\"The token is not valid\"", Assert.Single(response.Headers.WwwAuthenticate).ToString());
        Assert.Equal("The access token is not valid for this API.", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/openapi/v2.json")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task The_probe_the_documents_and_swagger_ui_need_no_token(string path)
    {
        using var response = await SendAsync("GET", path, token: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Authentication:Schemes:Bearer:ValidIssuer", "ValidIssuer")]
    [InlineData("Authentication:Schemes:Bearer:SigningKeys:0:Value", "SigningKeys")]
    public async Task The_api_refuses_to_start_without_its_token_settings(string setting, string named)
    {
        await using var factory = new OfflineApiFactory().WithWebHostBuilder(builder => builder.UseSetting(setting, ""));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains($"Authentication:Schemes:Bearer is missing {named}", exception.Message);
        Assert.Contains("dotnet user-jwts create", exception.Message);
    }

    [Fact]
    public void With_a_real_identity_provider_the_authority_replaces_the_issuer_and_the_keys_but_not_the_audience()
    {
        var withAuthority = Bearer(("Authority", "https://login.example/apexpost"), ("ValidAudiences:0", "api://apexpost-tracking"));
        var withoutAudience = Bearer(("Authority", "https://login.example/apexpost"));

        AuthenticationRegistration.RequireConfiguration(withAuthority);
        var exception = Assert.Throws<InvalidOperationException>(() => AuthenticationRegistration.RequireConfiguration(withoutAudience));

        Assert.StartsWith($"{AuthenticationRegistration.ConfigurationSection} is missing ValidAudiences.", exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_there_is_no_token_endpoint(string environment)
    {
        await using var factory = new OfflineApiFactory().WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(DevelopmentTokenEndpoint.Path, ApiClient.RawJson("""{ "name": "x", "roles": ["Admin"] }"""));

        // Not mapped at all: the fallback policy then asks for a token, and with one it is a 404.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("""{ "name": "Kofi", "roles": ["Driver"] }""", "roles")]
    [InlineData("""{ "name": "Kofi", "roles": [] }""", "roles")]
    [InlineData("""{ "name": "Kofi", "roles": ["Courier"] }""", "courierId")]
    [InlineData("""{ "roles": ["Admin"] }""", "name")]
    public async Task The_development_token_endpoint_refuses_unknown_roles_and_a_courier_without_an_id(string body, string field)
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        using var response = await client.PostAsync(DevelopmentTokenEndpoint.Path, ApiClient.RawJson(body));
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _), field);
    }

    private static async Task<HttpResponseMessage> SendAsync(string method, string path, string? token)
    {
        await using var factory = new OfflineApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method is "POST" or "PATCH" ? ApiClient.RawJson("{}") : null,
        };
        var response = await client.SendAsync(request);
        // Buffer the body before the host is disposed.
        await response.Content.LoadIntoBufferAsync();
        return response;
    }

    private static IConfigurationSection Bearer(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => KeyValuePair.Create($"{AuthenticationRegistration.ConfigurationSection}:{setting.Key}", (string?)setting.Value)))
            .Build()
            .GetSection(AuthenticationRegistration.ConfigurationSection);

    private static string Unsigned() => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = TestTokens.Issuer,
        Audience = TestTokens.Audience,
        Expires = DateTime.UtcNow.AddMinutes(30),
        Claims = new Dictionary<string, object> { ["role"] = ApiRoles.Admin },
    });

    private static string WithoutExpiry() => new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
    {
        Issuer = TestTokens.Issuer,
        Audience = TestTokens.Audience,
        Claims = new Dictionary<string, object> { ["role"] = ApiRoles.Admin },
        // This run's real key: only the missing expiry is wrong.
        SigningCredentials = TestTokens.Credentials,
    });
}
