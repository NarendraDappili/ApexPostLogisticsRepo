using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.Tests.Hosting;

namespace ApexPost.Api.Tests.Endpoints;

/// <summary>
/// B6: POST /api/shipments/{trackingNumber}/scans over HTTP on a real database. Re-sending a scan ID never creates a
/// duplicate, the response counts accepted, skipped and rejected scans, and parallel uploads of the same scans
/// still store each scan once.
/// </summary>
[Collection(SqlApiCollection.Name)]
public sealed class ScanUploadEndpointTests(SqlApiFactory factory)
{
    private static readonly DateTimeOffset Scanned = DateTimeOffset.UtcNow.AddHours(-2);
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task New_scans_are_accepted_in_scan_time_order_and_move_the_shipment()
    {
        var number = await NewShipmentAsync();
        var pickup = Scan("Pickup", Scanned);
        var transit = Scan(1, Scanned.AddMinutes(30));

        using var response = await UploadAsync(number, transit, pickup);
        var result = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((2, 0, 0, 2), Counts(result));
        Assert.Equal(0, result.GetProperty("rejections").GetArrayLength());
        Assert.Equal([Id(pickup), Id(transit)], await HistoryAsync(number));
        Assert.Equal(2, (await (await _client.GetAsync($"/api/shipments/{number}")).JsonAsync()).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Sending_the_same_upload_again_creates_no_duplicates()
    {
        var number = await NewShipmentAsync();
        object[] scans = [Scan("Pickup", Scanned), Scan("InTransit", Scanned.AddMinutes(30))];
        (await UploadAsync(number, scans)).Dispose();

        using var again = await UploadAsync(number, scans);
        var result = await again.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal((0, 2, 0, 2), Counts(result));
        Assert.Equal(2, (await HistoryAsync(number)).Length);
    }

    [Fact]
    public async Task Each_scan_is_counted_once_as_accepted_skipped_or_rejected_with_its_reason()
    {
        var number = await NewShipmentAsync();
        var pickup = Scan("Pickup", Scanned);
        (await UploadAsync(number, pickup)).Dispose();
        var transit = Scan("InTransit", Scanned.AddMinutes(30));
        var delivered = Scan("Delivered", Scanned.AddMinutes(60));

        using var response = await UploadAsync(number, pickup, transit, delivered);
        var result = await response.JsonAsync();

        Assert.Equal((1, 1, 1, 2), Counts(result));
        var rejection = result.GetProperty("rejections")[0];
        Assert.Equal(Id(delivered), rejection.GetProperty("scanId").GetString());
        Assert.Equal("A Delivered scan cannot be recorded: a shipment cannot move from InTransit to Delivered; the next status can only be OutForDelivery.",
            rejection.GetProperty("reason").GetString());
        Assert.Equal([Id(pickup), Id(transit)], await HistoryAsync(number));
    }

    [Fact]
    public async Task A_rejected_scan_is_accepted_once_the_shipment_is_ready_for_it()
    {
        var number = await NewShipmentAsync();
        var delivered = Scan("Delivered", Scanned.AddMinutes(90));
        await _client.AdvanceAsync(number, 2);
        (await UploadAsync(number, delivered)).Dispose();
        (await _client.PatchStatusAsync(number, "OutForDelivery")).Dispose();

        var retry = await (await UploadAsync(number, delivered)).JsonAsync();

        Assert.Equal((1, 0, 0, 4), Counts(retry));
    }

    [Fact]
    public async Task Parallel_uploads_of_the_same_scans_store_each_scan_once()
    {
        var number = await NewShipmentAsync();
        object[] scans = [Scan("Pickup", Scanned), Scan("InTransit", Scanned.AddMinutes(30))];

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => UploadAsync(number, scans)));
        try
        {
            // The race has one winner. Every other request either saw the scans already stored (200, duplicates) or
            // lost on the shipment's row version or the unique scan ID (409, safe to send again).
            Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            var accepted = 0;
            foreach (var response in responses.Where(response => response.StatusCode == HttpStatusCode.OK)) accepted += (await response.JsonAsync()).GetProperty("accepted").GetInt32();
            Assert.Equal(2, accepted);
            Assert.Equal(2, (await HistoryAsync(number)).Length);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task A_lost_race_is_a_409_that_says_to_send_again()
    {
        // Ten rounds of two parallel uploads: at least one 409 is very likely, and every 409 must read the same.
        for (var round = 0; round < 10; round++)
        {
            var number = await NewShipmentAsync();
            object[] scans = [Scan("Pickup", Scanned)];
            var responses = await Task.WhenAll(UploadAsync(number, scans), UploadAsync(number, scans));
            Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
            foreach (var conflict in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
            {
                Assert.Contains("Send the upload again", (await conflict.JsonAsync()).GetProperty("detail").GetString());
            }

            foreach (var response in responses) response.Dispose();
            Assert.Single(await HistoryAsync(number));
        }
    }

    [Fact]
    public async Task An_upload_for_an_unknown_shipment_is_404()
    {
        using var response = await UploadAsync(SqlApiFactory.NextTrackingNumber(), Scan("Pickup", Scanned));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_scan_time_in_the_future_fails_the_request_naming_the_scan()
    {
        var number = await NewShipmentAsync();

        using var response = await UploadAsync(number, Scan("Pickup", Scanned), Scan("InTransit", DateTimeOffset.UtcNow.AddHours(1)));
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["scannedAtUtc cannot be in the future; it is more than 5 minutes ahead of the server clock."],
            problem.GetProperty("errors").GetProperty("scans[1].scannedAtUtc").EnumerateArray().Select(error => error.GetString()));
        Assert.Empty(await HistoryAsync(number));
    }

    [Fact]
    public async Task Malformed_scans_and_repeated_ids_fail_the_request_with_json_field_keys()
    {
        var number = await NewShipmentAsync();
        var scan = Scan("Pickup", Scanned);

        var missing = await (await _client.PostAsync($"/api/shipments/{number}/scans", ApiClient.RawJson("""{ "scans": [ { "scanType": "Lost" } ] }"""))).JsonAsync();
        using var repeated = await UploadAsync(number, scan, scan);
        using var empty = await _client.PostAsJsonAsync($"/api/shipments/{number}/scans", new { scans = Array.Empty<object>() });

        Assert.True(missing.GetProperty("errors").TryGetProperty("scans[0].scanType", out _) || missing.GetProperty("errors").TryGetProperty("$.scans[0].scanType", out _));
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        Assert.Equal([$"Each scan ID can appear only once in an upload; repeated: {Id(scan)}."],
            (await repeated.JsonAsync()).GetProperty("errors").GetProperty("scans").EnumerateArray().Select(error => error.GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Empty(await HistoryAsync(number));
    }

    [Fact]
    public async Task A_scan_with_every_field_missing_names_each_one()
    {
        var number = await NewShipmentAsync();

        using var response = await _client.PostAsync($"/api/shipments/{number}/scans", ApiClient.RawJson("""{ "scans": [ {} ] }"""));
        var errors = (await response.JsonAsync()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["scans[0].courierId", "scans[0].location", "scans[0].scanId", "scans[0].scanType", "scans[0].scannedAtUtc"],
            errors.EnumerateObject().Select(error => error.Name).Order(StringComparer.Ordinal));
    }

    private async Task<string> NewShipmentAsync()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();
        return number;
    }

    private Task<HttpResponseMessage> UploadAsync(string trackingNumber, params object[] scans) =>
        _client.PostAsJsonAsync($"/api/shipments/{trackingNumber}/scans", new { scans });

    private async Task<string[]> HistoryAsync(string trackingNumber) =>
        [.. (await (await _client.GetAsync($"/api/shipments/{trackingNumber}/scans")).JsonAsync()).EnumerateArray().Select(scan => scan.GetProperty("scanId").GetString()!)];

    private static Dictionary<string, object?> Scan(object scanType, DateTimeOffset scannedAt) => new()
    {
        ["scanId"] = "scan::" + Guid.CreateVersion7(),
        ["scanType"] = scanType,
        ["location"] = "Depot 4",
        ["courierId"] = "CR-001",
        ["scannedAtUtc"] = scannedAt,
        ["notes"] = null,
    };

    private static string Id(Dictionary<string, object?> scan) => (string)scan["scanId"]!;

    private static (int Accepted, int Duplicates, int Rejected, int Status) Counts(JsonElement result) =>
        (result.GetProperty("accepted").GetInt32(), result.GetProperty("duplicatesSkipped").GetInt32(), result.GetProperty("rejected").GetInt32(), result.GetProperty("shipmentStatus").GetInt32());
}
