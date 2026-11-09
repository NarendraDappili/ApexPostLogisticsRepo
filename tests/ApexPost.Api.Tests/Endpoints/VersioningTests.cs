using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.Tests.Hosting;
using ApexPost.Api.Versioning;

namespace ApexPost.Api.Tests.Endpoints;

/// <summary>
/// C4: URL-segment versioning. v1 keeps the Part B contract and is deprecated, with its deprecation and sunset dates
/// in the headers of every successful response. v2 nests the recipient, adds a latestScan summary and writes enums
/// by name. Both versions read and write the same shipments through the same service. The Part B URLs without a
/// version still answer as v1.
/// </summary>
[Collection(SqlApiCollection.Name)]
public sealed class VersioningTests(SqlApiFactory factory)
{
    private const string Scan1 = "scan::7a1c0d4e-0000-4000-8000-000000000001";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task V2_create_is_201_with_a_v2_location_a_nested_recipient_a_status_name_and_no_scan_yet()
    {
        var number = SqlApiFactory.NextTrackingNumber();

        using var created = await _client.PostAsJsonAsync("/api/v2/shipments", ApiClient.NewShipment(number));
        var body = await created.JsonAsync();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/v2/shipments/{number}", created.Headers.Location!.AbsolutePath);
        Assert.Equal(["latestScan", "recipient", "status", "trackingNumber", "weightKg"], body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Created", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("latestScan").ValueKind);
        var recipient = body.GetProperty("recipient");
        Assert.Equal(("Ama Mensah", "+44 20 7946 0000", "12 Harbour Road", "Portside"),
            (recipient.GetProperty("name").GetString(), recipient.GetProperty("phone").GetString(), recipient.GetProperty("address").GetString(), recipient.GetProperty("city").GetString()));
    }

    [Fact]
    public async Task Both_versions_show_the_same_shipment_each_in_its_own_shape()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.PostAsJsonAsync("/api/v1/shipments", ApiClient.NewShipment(number))).EnsureSuccessStatusCode();
        // Sent newest first: the latest scan is the latest by scan time, not the last one in the request.
        (await _client.PostAsJsonAsync($"/api/v1/shipments/{number}/scans", new
        {
            scans = new object[]
            {
                Scan("scan::7a1c0d4e-0000-4000-8000-0000000000a2", "InTransit", "Northgate hub", "2026-11-02T11:00:00Z"),
                Scan("scan::7a1c0d4e-0000-4000-8000-0000000000a1", "Pickup", "Portside depot", "2026-11-02T08:30:00Z"),
            },
        })).EnsureSuccessStatusCode();

        var v1 = await (await _client.GetAsync($"/api/v1/shipments/{number}")).JsonAsync();
        var v2 = await (await _client.GetAsync($"/api/v2/shipments/{number}")).JsonAsync();

        Assert.Equal((2, "Ama Mensah"), (v1.GetProperty("status").GetInt32(), v1.GetProperty("recipientName").GetString()));
        Assert.False(v1.TryGetProperty("latestScan", out _));
        Assert.Equal(("InTransit", "Ama Mensah"), (v2.GetProperty("status").GetString(), v2.GetProperty("recipient").GetProperty("name").GetString()));
        var latest = v2.GetProperty("latestScan");
        Assert.Equal(["location", "scanType", "scannedAtUtc"], latest.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(("InTransit", "Northgate hub", DateTimeOffset.Parse("2026-11-02T11:00:00Z", CultureInfo.InvariantCulture)),
            (latest.GetProperty("scanType").GetString(), latest.GetProperty("location").GetString(), latest.GetProperty("scannedAtUtc").GetDateTimeOffset()));
    }

    [Fact]
    public async Task V2_writes_every_enum_by_name_in_lists_scan_histories_and_uploads()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        var city = $"V2City{number[^6..]}";
        (await _client.PostAsJsonAsync("/api/v2/shipments", ApiClient.NewShipment(number, city))).EnsureSuccessStatusCode();

        using var upload = await _client.PostAsJsonAsync($"/api/v2/shipments/{number}/scans", new { scans = new[] { Scan(Scan1.Replace("0001", number[^4..], StringComparison.Ordinal), "Pickup", "Portside depot", "2026-11-02T08:30:00Z") } });
        var uploaded = await upload.JsonAsync();
        var history = await (await _client.GetAsync($"/api/v2/shipments/{number}/scans")).JsonAsync();
        var page = await (await _client.GetAsync($"/api/v2/shipments?city={city}&status=PickedUp")).JsonAsync();

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal((1, "PickedUp"), (uploaded.GetProperty("accepted").GetInt32(), uploaded.GetProperty("shipmentStatus").GetString()));
        Assert.Equal("Pickup", Assert.Single(history.EnumerateArray()).GetProperty("scanType").GetString());
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(("PickedUp", "Pickup"), (item.GetProperty("status").GetString(), item.GetProperty("latestScan").GetProperty("scanType").GetString()));
    }

    [Fact]
    public async Task V2_errors_are_the_same_problem_details()
    {
        using var missing = await _client.GetAsync($"/api/v2/shipments/{SqlApiFactory.NextTrackingNumber()}");
        using var invalid = await _client.PostAsJsonAsync("/api/v2/shipments", new { });

        Assert.Equal((HttpStatusCode.NotFound, "application/problem+json"), (missing.StatusCode, missing.Content.Headers.ContentType!.MediaType));
        Assert.Equal((HttpStatusCode.BadRequest, "application/problem+json"), (invalid.StatusCode, invalid.Content.Headers.ContentType!.MediaType));
        Assert.True((await invalid.JsonAsync()).GetProperty("errors").TryGetProperty("trackingNumber", out _));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Every_response_reports_the_supported_and_deprecated_versions(string version)
    {
        using var response = await _client.GetAsync($"/api/{version}/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2.0", Header(response, "api-supported-versions"));
        Assert.Equal("1.0", Header(response, "api-deprecated-versions"));
    }

    [Fact]
    public async Task V1_responses_carry_its_deprecation_and_sunset_dates_with_a_link_to_the_policy()
    {
        using var response = await _client.GetAsync("/api/v1/shipments");

        // RFC 9745: "@" and Unix seconds. RFC 8594: an HTTP date.
        Assert.Equal($"@{ApiVersions.V1DeprecatedOn.ToUnixTimeSeconds()}", Header(response, "Deprecation"));
        Assert.Equal(ApiVersions.V1SunsetOn, DateTimeOffset.Parse(Header(response, "Sunset")!, CultureInfo.InvariantCulture));
        var links = response.Headers.GetValues("Link").SelectMany(value => value.Split(','));
        Assert.Contains(links, link => link.Contains(ApiVersions.PolicyUrl, StringComparison.Ordinal) && link.Contains("rel=\"sunset\"", StringComparison.Ordinal));
        Assert.Contains(links, link => link.Contains(ApiVersions.PolicyUrl, StringComparison.Ordinal) && link.Contains("rel=\"deprecation\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task V2_responses_carry_no_deprecation_or_sunset()
    {
        using var response = await _client.GetAsync("/api/v2/shipments");

        Assert.False(response.Headers.Contains("Deprecation"));
        Assert.False(response.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task The_part_b_urls_without_a_version_still_answer_as_v1()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        var city = $"LegacyCity{number[^6..]}";

        using var created = await _client.PostAsJsonAsync("/api/shipments", ApiClient.NewShipment(number, city));
        using var legacy = await _client.GetAsync($"/api/shipments/{number}");
        using var v1 = await _client.GetAsync($"/api/v1/shipments/{number}");
        var page = await (await _client.GetAsync($"/api/shipments?city={city}&pageSize=1")).JsonAsync();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        // The new URL to use from now on.
        Assert.Equal($"/api/v1/shipments/{number}", created.Headers.Location!.AbsolutePath);
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal((await v1.JsonAsync()).ToString(), (await legacy.JsonAsync()).ToString());
        Assert.NotNull(Header(legacy, "Sunset"));
        // The query string survives the rewrite.
        Assert.Equal((1, 1), (page.GetProperty("totalCount").GetInt32(), page.GetProperty("pageSize").GetInt32()));
    }

    [Theory]
    [InlineData("/api/v3/shipments")]
    [InlineData("/api/v0/shipments")]
    [InlineData("/api/shipmentsx")]
    public async Task An_unknown_version_or_path_is_a_404_problem(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task The_version_may_also_be_written_with_its_minor_number()
    {
        using var response = await _client.GetAsync("/api/v2.0/shipments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object Scan(string scanId, string scanType, string location, string scannedAtUtc) =>
        new { scanId, scanType, location, courierId = "CR-1042", scannedAtUtc };

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
