using System.Net;
using ApexPost.Api.Security;
using ApexPost.Api.Tests.Endpoints;
using ApexPost.Api.Tests.Hosting;
using ApexPost.Api.Tests.OpenApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApexPost.Api.Tests.Security;

/// <summary>
/// D3: the partner route takes an X-Api-Key header instead of a bearer token, checked by an endpoint filter. A
/// missing or invalid key is a 401 ProblemDetails before the shipment is even looked up, so none of these requests
/// reaches the database. The keys come from configuration, are checked at start-up and are never logged.
/// </summary>
public sealed class PartnerApiKeyTests
{
    private const string Route = "/api/v2/partner/shipments/APX-24000001";

    [Fact]
    public async Task Without_a_key_the_partner_route_is_a_401_problem_that_names_the_header()
    {
        using var response = await SendAsync();
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(PartnerApiKeyFilter.Challenge, Assert.Single(response.Headers.WwwAuthenticate).ToString());
        Assert.Equal((PartnerApiKeyFilter.UnauthorizedTitle, 401), (problem.GetProperty("title").GetString(), problem.GetProperty("status").GetInt32()));
        Assert.Equal("Send your partner API key in the X-Api-Key header.", problem.GetProperty("detail").GetString());
        Assert.Equal($"GET {Route}", problem.GetProperty("instance").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    // The cases are named, not given as keys: the keys are random per test process, and a test runner such as Visual
    // Studio's enumerates theory data in one process and runs it in another, where the keys are different.
    [Theory]
    [InlineData("an unknown key")]
    [InlineData("a known key in another case")]
    [InlineData("a known key with a character more")]
    [InlineData("a known key cut short")]
    [InlineData("two keys at once")]
    public async Task An_invalid_key_is_a_401(string why)
    {
        string[] keys = why switch
        {
            "an unknown key" => [TestPartnerKeys.NewKey()],
            "a known key in another case" => [TestPartnerKeys.NorthwindKey.ToLowerInvariant()],
            "a known key with a character more" => [TestPartnerKeys.NorthwindKey + "A"],
            "a known key cut short" => [TestPartnerKeys.NorthwindKey[..^1]],
            "two keys at once" => [TestPartnerKeys.NorthwindKey, TestPartnerKeys.ContosoKey],
            _ => throw new ArgumentOutOfRangeException(nameof(why), why, null),
        };

        using var response = await SendAsync(keys);
        var problem = await response.JsonAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, why);
        Assert.Equal("The API key in the X-Api-Key header is not valid. Check it with ApexPost partner support.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_key_in_the_query_string_is_not_accepted()
    {
        using var response = await SendAsync([], $"{Route}?apiKey={Uri.EscapeDataString(TestPartnerKeys.NorthwindKey)}&X-Api-Key={Uri.EscapeDataString(TestPartnerKeys.NorthwindKey)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_bearer_token_does_not_open_the_partner_route()
    {
        // The test clients send a valid Courier and Admin token by default; it counts for nothing here.
        using var response = await SendAsync([], Route, keepToken: true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(PartnerApiKeyFilter.Challenge, Assert.Single(response.Headers.WwwAuthenticate).ToString());
    }

    [Fact]
    public async Task A_partner_key_opens_no_other_route()
    {
        using var response = await SendAsync([TestPartnerKeys.NorthwindKey], "/api/v2/shipments/APX-24000001");

        // The rest of the API still wants a bearer token.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
    }

    [Fact]
    public async Task Refused_requests_are_logged_without_the_key()
    {
        var logs = new CapturingLoggerProvider();
        var wrongKey = TestPartnerKeys.NewKey();

        using (await SendAsync([wrongKey], Route, logs: logs))
        {
        }

        var refusal = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Partner request refused", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, refusal.Level);
        Assert.Equal($"Partner request refused: an invalid API key for GET {Route}", refusal.Message);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(wrongKey, StringComparison.Ordinal)
            || entry.Message.Contains(TestPartnerKeys.NorthwindKey, StringComparison.Ordinal)
            || entry.Message.Contains(TestPartnerKeys.ContosoKey, StringComparison.Ordinal));
    }

    // NorthwindKey stands for this process's Northwind key, read when the test runs (see An_invalid_key_is_a_401).
    [Theory]
    [InlineData("PartnerApi:Keys:0:Key", "too-short-a-key", "PartnerApi:Keys:0:Key is shorter than 32 characters")]
    [InlineData("PartnerApi:Keys:0:Partner", "", "PartnerApi:Keys:0:Partner is missing")]
    [InlineData("PartnerApi:Keys:1:Key", "NorthwindKey", "two entries have the same key")]
    public async Task The_api_refuses_to_start_with_a_malformed_key_and_does_not_say_the_key(string setting, string value, string message)
    {
        if (value == "NorthwindKey") value = TestPartnerKeys.NorthwindKey;
        await using var factory = new OfflineApiFactory().WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(message, exception.Message);
        Assert.DoesNotContain(TestPartnerKeys.NorthwindKey, exception.Message);
        if (value.Length > 0) Assert.DoesNotContain(value, exception.Message);
    }

    [Fact]
    public void Without_any_keys_every_partner_request_is_refused_and_a_warning_says_so()
    {
        var logs = new CapturingLoggerProvider();

        var keys = new PartnerApiKeys(Options.Create(new PartnerApiKeyOptions()), new Logger<PartnerApiKeys>(new LoggerFactory([logs])));

        Assert.Null(keys.FindPartner(TestPartnerKeys.NorthwindKey));
        Assert.Null(keys.FindPartner(""));
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith("No partner API keys are configured", StringComparison.Ordinal));
    }

    [Fact]
    public void A_partner_can_have_two_keys_while_one_is_rotated()
    {
        string oldKey = TestPartnerKeys.NewKey(), newKey = TestPartnerKeys.NewKey();
        var options = new PartnerApiKeyOptions { Keys = [new() { Partner = "Northwind", Key = oldKey }, new() { Partner = "Northwind", Key = newKey }] };

        var keys = new PartnerApiKeys(Options.Create(options), new Logger<PartnerApiKeys>(new LoggerFactory()));

        Assert.Equal(("Northwind", "Northwind", null), (keys.FindPartner(oldKey), keys.FindPartner(newKey), keys.FindPartner(TestPartnerKeys.NewKey())));
    }

    [Fact]
    public async Task The_v2_document_asks_for_the_key_on_the_partner_route_only()
    {
        var v2 = await OpenApiMetadataTests.DocumentAsync("v2");
        var v1 = await OpenApiMetadataTests.DocumentAsync("v1");

        var scheme = v2.GetProperty("components").GetProperty("securitySchemes").GetProperty("PartnerApiKey");
        Assert.Equal(("apiKey", "header", "X-Api-Key"), (scheme.GetProperty("type").GetString(), scheme.GetProperty("in").GetString(), scheme.GetProperty("name").GetString()));
        var partner = OpenApiMetadataTests.Operation(v2, "GET /api/v2/partner/shipments/{trackingNumber}");
        Assert.Equal(["PartnerApiKey"], partner.GetProperty("security").EnumerateArray().SelectMany(requirement => requirement.EnumerateObject().Select(item => item.Name)));
        Assert.Equal("No valid partner API key in the X-Api-Key header.", partner.GetProperty("responses").GetProperty("401").GetProperty("description").GetString());
        // Every other operation keeps the document's Bearer requirement, and v1 has no partner route and no key.
        Assert.All(OpenApiMetadataTests.AllOperations(v2).Where(operation => !operation.Route.Contains("/partner/", StringComparison.Ordinal)),
            operation => Assert.False(operation.Value.TryGetProperty("security", out _), operation.Route));
        Assert.False(v1.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("PartnerApiKey", out _));
    }

    private static async Task<HttpResponseMessage> SendAsync(string[]? keys = null, string path = Route, bool keepToken = false, CapturingLoggerProvider? logs = null)
    {
        await using var factory = logs is null
            ? new OfflineApiFactory()
            : new OfflineApiFactory().WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = factory.CreateClient();
        if (!keepToken) client.DefaultRequestHeaders.Authorization = null;
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        foreach (var key in keys ?? []) request.Headers.Add(PartnerApiKeyFilter.HeaderName, key);
        var response = await client.SendAsync(request);
        // Buffer the body before the host is disposed.
        await response.Content.LoadIntoBufferAsync();
        return response;
    }
}
