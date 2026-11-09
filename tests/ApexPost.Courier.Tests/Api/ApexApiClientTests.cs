using System.Net;
using System.Text.Json;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ApexPost.Courier.Tests.Api;

/// <summary>
/// D6: the courier app reaches the v2 API through a typed ApexApiClient made by IHttpClientFactory, with the base
/// address from configuration and the courier's token added by AuthHeaderHandler. The network is a stub; the app's
/// whole composition (CourierAppHost) is real.
/// </summary>
public sealed class ApexApiClientTests : IDisposable
{
    private static readonly DateTimeOffset ScannedAt = new(2026, 11, 9, 8, 30, 0, TimeSpan.Zero);
    private readonly CourierAppRun _app = new();
    private readonly StubServer _server = new();

    public ApexApiClientTests()
    {
        // A sign-in answer by default: these tests look at what is sent, and an answer without a token is refused (D8).
        _server.Respond = _ => StubServer.Json(HttpStatusCode.OK, """{ "accessToken": "eyJ.x.y", "tokenType": "Bearer", "expiresAtUtc": "2026-11-09T13:00:00+00:00" }""");
    }

    public void Dispose()
    {
        _app.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task A_scan_upload_posts_the_scans_as_v2_json_with_the_couriers_token()
    {
        _server.Respond = _ => StubServer.Json(HttpStatusCode.OK,
            """{ "accepted": 1, "duplicatesSkipped": 1, "rejected": 0, "rejections": [], "shipmentStatus": "PickedUp" }""");
        using var host = Host();
        await host.Services.GetRequiredService<ITokenStore>().SaveAsync("courier-token");
        var pickup = new ShipmentScan(ScanId.Parse("scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10"), "APX-30000001", ScanType.Pickup, "CR-1042", "Riverside Depot", ScannedAt, "Front desk", SyncStatus.Pending);
        var transit = new ShipmentScan(ScanId.Parse("scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d11"), "APX-30000001", ScanType.InTransit, "CR-1042", "North Hub", ScannedAt.AddHours(1), null, SyncStatus.Pending);

        var result = await host.Services.GetRequiredService<ApexApiClient>().UploadScansAsync("APX-30000001", [pickup, transit]);

        var request = Assert.Single(_server.Requests);
        Assert.Equal((HttpMethod.Post, new Uri("https://localhost:7245/api/v2/shipments/APX-30000001/scans")), (request.Method, request.Uri));
        Assert.Equal("Bearer courier-token", request.Authorization);
        Assert.Equal(
            """{"scans":[{"scanId":"scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d10","scanType":"Pickup","location":"Riverside Depot","courierId":"CR-1042","scannedAtUtc":"2026-11-09T08:30:00+00:00","notes":"Front desk"},"""
            + """{"scanId":"scan::0199c1d2-7a4e-7b11-9c3d-2f6a8b1e4d11","scanType":"InTransit","location":"North Hub","courierId":"CR-1042","scannedAtUtc":"2026-11-09T09:30:00+00:00","notes":null}]}""",
            request.Body);
        Assert.Equal((1, 1, 0, 0, "PickedUp"), (result.Accepted, result.DuplicatesSkipped, result.Rejected, result.Rejections!.Count, result.ShipmentStatus));
    }

    [Fact]
    public async Task Without_a_stored_token_no_authorization_header_is_sent()
    {
        using var host = Host();

        await host.Services.GetRequiredService<ApexApiClient>().GetDevelopmentTokenAsync("CR-1042");

        Assert.Null(Assert.Single(_server.Requests).Authorization);
    }

    [Fact]
    public async Task Signing_out_takes_effect_on_the_next_request()
    {
        using var host = Host();
        var tokens = host.Services.GetRequiredService<ITokenStore>();
        var client = host.Services.GetRequiredService<ApexApiClient>();

        await tokens.SaveAsync("first");
        await client.GetDevelopmentTokenAsync("CR-1042");
        await tokens.ClearAsync();
        await client.GetDevelopmentTokenAsync("CR-1042");

        Assert.Equal(["Bearer first", null], _server.Requests.Select(request => request.Authorization));
    }

    [Fact]
    public async Task A_development_token_is_asked_for_with_the_courier_role_and_id()
    {
        _server.Respond = _ => StubServer.Json(HttpStatusCode.OK, """{ "accessToken": "eyJ.x.y", "tokenType": "Bearer", "expiresAtUtc": "2026-11-09T13:00:00+00:00" }""");
        using var host = Host();

        var token = await host.Services.GetRequiredService<ApexApiClient>().GetDevelopmentTokenAsync("CR-1042");

        var request = Assert.Single(_server.Requests);
        Assert.Equal(new Uri("https://localhost:7245/dev/token"), request.Uri);
        Assert.Equal("""{"name":"CR-1042","roles":["Courier"],"courierId":"CR-1042"}""", request.Body);
        Assert.Equal(("eyJ.x.y", new DateTimeOffset(2026, 11, 9, 13, 0, 0, TimeSpan.Zero)), (token.AccessToken, token.ExpiresAtUtc));
    }

    [Fact]
    public async Task An_error_status_becomes_an_api_exception_with_that_status()
    {
        _server.Respond = _ => StubServer.Json(HttpStatusCode.Forbidden, """{ "title": "Access is denied.", "status": 403 }""", "application/problem+json");
        using var host = Host();

        var exception = await Assert.ThrowsAsync<ApexApiException>(() => host.Services.GetRequiredService<ApexApiClient>().UploadScansAsync("APX-30000001", []));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Theory]
    [InlineData("https://api.apexpost.example", "https://api.apexpost.example/api/v2/shipments/APX-30000001/scans")]
    [InlineData("http://localhost:5245/", "http://localhost:5245/api/v2/shipments/APX-30000001/scans")]
    [InlineData("https://gateway.apexpost.example/tracking", "https://gateway.apexpost.example/tracking/api/v2/shipments/APX-30000001/scans")]
    public async Task The_api_address_comes_from_configuration(string baseAddress, string expected)
    {
        using var host = Host($"--ApexPostApi:BaseAddress={baseAddress}");

        await host.Services.GetRequiredService<ApexApiClient>().UploadScansAsync("APX-30000001", []);

        Assert.Equal(new Uri(expected), Assert.Single(_server.Requests).Uri);
    }

    [Theory]
    [InlineData("http://api.apexpost.example/", "must use https://")]
    [InlineData("http://192.168.1.20:5245/", "must use https://")]
    [InlineData("/api", "must be an absolute URL")]
    public void A_wrong_api_address_stops_the_app_with_the_reason(string baseAddress, string reason)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Host($"--ApexPostApi:BaseAddress={baseAddress}"));

        Assert.Contains($"ApexPostApi:BaseAddress {reason}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_typed_client_comes_from_the_factory_and_the_app_never_makes_an_httpclient()
    {
        using var host = Host();

        var first = host.Services.GetRequiredService<ApexApiClient>();
        var second = host.Services.GetRequiredService<ApexApiClient>();

        Assert.NotSame(first, second);
        var offenders = Directory.EnumerateFiles(RepositoryFiles.PathOf("src", "ApexPost.Courier"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("new HttpClient(", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public async Task The_token_is_never_sent_over_plain_http_to_another_machine()
    {
        var tokens = new InMemoryTokenStore();
        await tokens.SaveAsync("courier-token");
        using var client = new HttpMessageInvoker(new AuthHeaderHandler(tokens) { InnerHandler = _server });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api.apexpost.example/api/v2/shipments"), CancellationToken.None));

        Assert.DoesNotContain("courier-token", exception.Message, StringComparison.Ordinal);
        Assert.Empty(_server.Requests);
    }

    private IHost Host(params string[] settings) => _app.CreateHost(TextReader.Null, TextWriter.Null, settings, _server.Install);
}
