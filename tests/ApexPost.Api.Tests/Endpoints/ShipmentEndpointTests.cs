using System.Net;
using System.Net.Http.Json;
using System.Text;
using ApexPost.Api.Tests.Hosting;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Api.Tests.Endpoints;

/// <summary>
/// B3: every shipment endpoint over HTTP against a real database, with the success code and the 400, 404, 409 and
/// 415 answers each one can give.
/// </summary>
[Collection(SqlApiCollection.Name)]
public sealed class ShipmentEndpointTests(SqlApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_is_201_with_a_location_that_reads_the_same_shipment()
    {
        var number = SqlApiFactory.NextTrackingNumber();

        using var created = await _client.CreateAsync(number, weightKg: 69.5m);
        var body = await created.JsonAsync();
        using var fetched = await _client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"/api/v1/shipments/{number}", created.Headers.Location!.AbsolutePath);
        Assert.Equal((number, 0, 69.5m, "Ama Mensah", "Portside"),
            (body.GetProperty("trackingNumber").GetString(), body.GetProperty("status").GetInt32(), body.GetProperty("weightKg").GetDecimal(),
             body.GetProperty("recipientName").GetString(), body.GetProperty("city").GetString()));
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(body.ToString(), (await fetched.JsonAsync()).ToString());
    }

    [Fact]
    public async Task Creating_a_tracking_number_twice_is_409_problem_details()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();

        using var response = await _client.CreateAsync(number);
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        Assert.Equal($"A shipment with the tracking number {number} already exists.", problem.GetProperty("detail").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task An_out_of_range_weight_is_400_with_the_field()
    {
        using var response = await _client.CreateAsync(SqlApiFactory.NextTrackingNumber(), weightKg: 80m);
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["weightKg must be between 0.1 and 70 kg."], problem.GetProperty("errors").GetProperty("weightKg").EnumerateArray().Select(error => error.GetString()));
    }

    [Fact]
    public async Task Missing_fields_and_malformed_json_are_400()
    {
        using var missing = await _client.PostAsync("/api/v1/shipments", ApiClient.RawJson("""{ "trackingNumber": "APX-00000001" }"""));
        using var malformed = await _client.PostAsync("/api/v1/shipments", ApiClient.RawJson("""{ "trackingNumber": """));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.True((await missing.JsonAsync()).GetProperty("errors").TryGetProperty("recipientName", out _));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_415()
    {
        using var response = await _client.PostAsync("/api/v1/shipments", new StringContent("trackingNumber=APX-00000001", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_tracking_number_is_404_problem_details()
    {
        var number = SqlApiFactory.NextTrackingNumber();

        using var response = await _client.GetAsync($"/api/v1/shipments/{number}");
        var problem = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal($"No shipment has the tracking number {number}.", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("/api/v1/shipments/APX-123")]
    [InlineData("/api/v1/shipments/not-a-number")]
    [InlineData("/api/v1/shipments/APX-12345678'%20OR%20'1'='1")]
    public async Task A_malformed_tracking_number_never_reaches_an_action(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_list_pages_and_filters_by_status_and_city()
    {
        var city = "Lister " + Guid.NewGuid().ToString("N")[..8];
        var numbers = Enumerable.Range(0, 3).Select(_ => SqlApiFactory.NextTrackingNumber()).ToArray();
        foreach (var number in numbers) (await _client.CreateAsync(number, city)).Dispose();
        await _client.AdvanceAsync(numbers[2], 1);

        var all = await (await _client.GetAsync($"/api/v1/shipments?city={Uri.EscapeDataString(city.ToUpperInvariant())}&pageSize=2")).JsonAsync();
        var second = await (await _client.GetAsync($"/api/v1/shipments?city={Uri.EscapeDataString(city)}&pageSize=2&page=2")).JsonAsync();
        var byName = await (await _client.GetAsync($"/api/v1/shipments?city={Uri.EscapeDataString(city)}&status=PickedUp")).JsonAsync();
        var byNumber = await (await _client.GetAsync($"/api/v1/shipments?city={Uri.EscapeDataString(city)}&status=0")).JsonAsync();

        Assert.Equal((1, 2, 3, 2), (all.GetProperty("page").GetInt32(), all.GetProperty("pageSize").GetInt32(), all.GetProperty("totalCount").GetInt32(), all.GetProperty("totalPages").GetInt32()));
        Assert.Equal(numbers[..2], Numbers(all));
        Assert.Equal([numbers[2]], Numbers(second));
        Assert.Equal([numbers[2]], Numbers(byName));
        Assert.Equal(numbers[..2], Numbers(byNumber));
    }

    [Fact]
    public async Task The_list_defaults_to_20_per_page()
    {
        var page = await (await _client.GetAsync("/api/v1/shipments")).JsonAsync();

        Assert.Equal((1, 20), (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32()));
    }

    [Theory]
    [InlineData("pageSize=51", "pageSize")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("page=0", "page")]
    public async Task A_page_outside_the_limits_is_400(string query, string field)
    {
        using var response = await _client.GetAsync($"/api/v1/shipments?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Theory]
    [InlineData("status=Lost")]
    [InlineData("page=abc")]
    public async Task A_query_value_of_the_wrong_type_is_400(string query)
    {
        using var response = await _client.GetAsync($"/api/v1/shipments?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_replaces_the_details_and_returns_200()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();

        using var response = await _client.PutAsJsonAsync($"/api/v1/shipments/{number}",
            new { recipientName = "Kofi Mensah", recipientPhone = "020 7946 0001", recipientAddress = "3 Quay Street", city = "Rivermouth", weightKg = 12.5m });
        var body = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("Kofi Mensah", "020 7946 0001", "Rivermouth", 12.5m),
            (body.GetProperty("recipientName").GetString(), body.GetProperty("recipientPhone").GetString(), body.GetProperty("city").GetString(), body.GetProperty("weightKg").GetDecimal()));
    }

    [Fact]
    public async Task Put_on_a_delivered_shipment_is_409_and_on_an_unknown_one_404()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();
        await _client.AdvanceAsync(number, 4);
        var details = new { recipientName = "Kofi Mensah", recipientPhone = "020 7946 0001", recipientAddress = "3 Quay Street", city = "Rivermouth", weightKg = 1m };

        using var delivered = await _client.PutAsJsonAsync($"/api/v1/shipments/{number}", details);
        using var unknown = await _client.PutAsJsonAsync($"/api/v1/shipments/{SqlApiFactory.NextTrackingNumber()}", details);

        Assert.Equal(HttpStatusCode.Conflict, delivered.StatusCode);
        Assert.Equal("The shipment is Delivered; its details can no longer change.", (await delivered.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Patch_status_moves_along_the_chain_by_name_or_number()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();

        using var byName = await _client.PatchStatusAsync(number, "PickedUp");
        using var byNumber = await _client.PatchStatusAsync(number, 2);

        Assert.Equal(HttpStatusCode.OK, byName.StatusCode);
        Assert.Equal(1, (await byName.JsonAsync()).GetProperty("status").GetInt32());
        Assert.Equal(2, (await byNumber.JsonAsync()).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Patch_status_that_skips_a_stage_is_409_naming_the_allowed_status()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();

        using var response = await _client.PatchStatusAsync(number, "Delivered");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("A shipment cannot move from Created to Delivered. The next status can only be PickedUp.", (await response.JsonAsync()).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("""{ "status": "Lost" }""")]
    [InlineData("""{ "status": 42 }""")]
    public async Task Patch_with_an_unknown_status_is_400(string json)
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();

        using var response = await _client.PatchAsync($"/api/v1/shipments/{number}/status", ApiClient.RawJson(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_of_a_created_shipment_is_204_and_it_is_gone()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();

        using var deleted = await _client.DeleteAsync($"/api/v1/shipments/{number}");
        using var fetched = await _client.GetAsync($"/api/v1/shipments/{number}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, deleted.Content.Headers.ContentLength ?? 0);
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }

    [Fact]
    public async Task Delete_after_pickup_is_409_and_of_an_unknown_shipment_404()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();
        await _client.AdvanceAsync(number, 1);

        using var pickedUp = await _client.DeleteAsync($"/api/v1/shipments/{number}");
        using var unknown = await _client.DeleteAsync($"/api/v1/shipments/{SqlApiFactory.NextTrackingNumber()}");

        Assert.Equal(HttpStatusCode.Conflict, pickedUp.StatusCode);
        Assert.Equal("Only a shipment in Created status can be deleted; this one is PickedUp.", (await pickedUp.JsonAsync()).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task The_scan_history_of_a_shipment_without_scans_is_empty_and_of_an_unknown_one_404()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();

        var empty = await (await _client.GetAsync($"/api/v1/shipments/{number}/scans")).JsonAsync();
        using var unknown = await _client.GetAsync($"/api/v1/shipments/{SqlApiFactory.NextTrackingNumber()}/scans");

        Assert.Equal(0, empty.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task The_scan_history_lists_recorded_scans_oldest_first()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        (await _client.CreateAsync(number)).Dispose();
        var scannedAt = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
        var pickup = "scan::" + Guid.CreateVersion7();
        var transit = "scan::" + Guid.CreateVersion7();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // Uploads arrive in step B6; until then the scans are recorded through the domain directly. The later
            // scan is added first, so the order comes from the query, not from insertion.
            var db = scope.ServiceProvider.GetRequiredService<ApexPostDbContext>();
            var shipment = await db.Set<TrackedShipment>().SingleAsync(row => row.TrackingNumber == new TrackingNumber(number));
            var pickupScan = shipment.RecordScan(new TrackingScanId(pickup), TrackingScanType.Pickup, "Depot 4", "CR-001", scannedAt, "Front desk", scannedAt.AddHours(1));
            var transitScan = shipment.RecordScan(new TrackingScanId(transit), TrackingScanType.InTransit, "Central Sorting", "CR-002", scannedAt.AddMinutes(30), null, scannedAt.AddHours(1));
            db.Add(transitScan);
            db.Add(pickupScan);
            await db.SaveChangesAsync();
        }

        var history = await (await _client.GetAsync($"/api/v1/shipments/{number}/scans")).JsonAsync();

        Assert.Equal([pickup, transit], history.EnumerateArray().Select(scan => scan.GetProperty("scanId").GetString()));
        var first = history[0];
        Assert.Equal((0, "Depot 4", "CR-001", "Front desk", scannedAt),
            (first.GetProperty("scanType").GetInt32(), first.GetProperty("location").GetString(), first.GetProperty("courierId").GetString(),
             first.GetProperty("notes").GetString(), first.GetProperty("scannedAtUtc").GetDateTimeOffset()));
    }

    private static string[] Numbers(System.Text.Json.JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("trackingNumber").GetString()!)];
}
