using System.Net;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Tests;

/// <summary>
/// Out for Delivery: scans have no such type and the server takes a Delivered scan only from that status, so the app
/// tells the server itself. A whole delivery then works from the courier app alone. The API is the in-memory FakeApi,
/// with the real API's status chain turned on where it matters.
/// </summary>
public sealed class OutForDeliveryTests : IDisposable
{
    private const string Number = "APX-30000001";
    private readonly CourierAppRun _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task It_patches_the_status_with_the_couriers_token()
    {
        var output = await _app.RunAsync(["CR-001", "", "5", "apx-30000001", "7"]);

        var request = Assert.Single(_app.Api.Server.Requests, request => request.Method == HttpMethod.Patch);
        Assert.Equal(new Uri($"https://localhost:7245/api/v2/shipments/{Number}/status"), request.Uri);
        Assert.Equal("""{"status":"OutForDelivery"}""", request.Body);
        Assert.StartsWith("Bearer fake-token-CR-001", request.Authorization, StringComparison.Ordinal);
        Assert.Contains($"{Number} is out for delivery.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_whole_delivery_works_from_the_app_alone()
    {
        _app.Api.EnforceStatusChain = true;
        IReadOnlyList<ShipmentScan> scans = [];

        var output = await _app.RunAsync(
            [
                "CR-001", "",
                "1", Number, "1", "Riverside Depot", "", "",  // Pickup
                "1", Number, "2", "North Hub", "", "",        // InTransit
                "5", Number,                                  // Out for Delivery, which sends the two scans first
                "1", Number, "3", "12 Harbour Road", "", "",  // Delivered
                "3",
                "7",
            ],
            services => scans = CourierAppRun.Scans(services));

        Assert.Contains($"{Number} is out for delivery.", output, StringComparison.Ordinal);
        Assert.Contains("Sync finished: 1 sent, 1 synced, 0 failed, 0 still pending.", output, StringComparison.Ordinal);
        Assert.Equal(3, scans.Count);
        Assert.All(scans, scan => Assert.Equal(SyncStatus.Synced, scan.SyncStatus));
        Assert.Equal("Delivered", _app.Api.StatusOf(Number));
    }

    [Fact]
    public async Task Without_it_the_server_refuses_the_delivery_and_says_why()
    {
        _app.Api.EnforceStatusChain = true;

        var output = await _app.RunAsync(["CR-001", "", "1", Number, "1", "Depot", "", "", "1", Number, "2", "Hub", "", "", "1", Number, "3", "12 Harbour Road", "", "", "3", "7"]);

        Assert.Contains("the next status can only be OutForDelivery.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pending_scans_go_to_the_server_before_the_status_change()
    {
        _app.Api.EnforceStatusChain = true;

        await _app.RunAsync(["CR-001", "", "1", Number, "1", "Depot", "", "", "1", Number, "2", "Hub", "", "", "5", Number, "7"]);

        var writes = _app.Api.Server.Requests.Where(request => !request.Uri.AbsolutePath.EndsWith("/dev/token", StringComparison.Ordinal)).Select(request => request.Method).ToList();
        Assert.Equal([HttpMethod.Post, HttpMethod.Patch], writes);
        Assert.Equal("OutForDelivery", _app.Api.StatusOf(Number));
    }

    [Fact]
    public async Task Without_a_network_nothing_is_sent_and_the_courier_is_told()
    {
        _app.Network.IsAvailable = false;

        var output = await _app.RunAsync(["CR-001", "", "5", "7"]);

        Assert.Contains("No network: the server was not told.", output, StringComparison.Ordinal);
        Assert.DoesNotContain(_app.Api.Server.Requests, request => request.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task Signed_in_offline_nothing_is_sent_and_the_courier_is_told()
    {
        _app.Api.Down = true;

        var output = await _app.RunAsync(["CR-001", "", "5", "7"]);

        Assert.Contains("The server was not told: you are signed in offline.", output, StringComparison.Ordinal);
        Assert.DoesNotContain(_app.Api.Server.Requests, request => request.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task A_bad_tracking_number_is_explained_and_nothing_is_sent()
    {
        var output = await _app.RunAsync(["CR-001", "", "5", "APX-1", "7"]);

        Assert.Contains("Not sent: A tracking number must be APX- followed by eight digits.", output, StringComparison.Ordinal);
        Assert.DoesNotContain(_app.Api.Server.Requests, request => request.Method == HttpMethod.Patch);
    }

    [Fact]
    public async Task A_status_the_server_refuses_is_shown_in_its_own_words()
    {
        var output = await _app.RunAsync(["CR-001", "", "5", Number, "5", Number, "7"]);

        Assert.Contains("The status was not changed: A shipment cannot move from OutForDelivery to OutForDelivery.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_shipment_is_explained()
    {
        _app.Api.UnknownShipments.Add(Number);

        var output = await _app.RunAsync(["CR-001", "", "5", Number, "7"]);

        Assert.Contains($"The status was not changed: No shipment has the tracking number {Number}.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_server_error_is_not_retried_because_a_patch_is_not_idempotent()
    {
        _app.Api.Server.Respond = request => request.Method == HttpMethod.Patch
            ? Api.StubServer.Json(HttpStatusCode.ServiceUnavailable, "{}", "application/problem+json")
            : Api.StubServer.Json(HttpStatusCode.OK, """{ "accessToken": "fake-token-CR-001#1", "tokenType": "Bearer", "expiresAtUtc": "2099-01-01T00:00:00+00:00" }""");

        var output = await _app.RunAsync(["CR-001", "", "5", Number, "7"]);

        Assert.Single(_app.Api.Server.Requests, request => request.Method == HttpMethod.Patch);
        Assert.Contains("The status was not changed: The server had a problem", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_401_goes_back_to_login_without_saying_it_twice()
    {
        _app.Api.RevokedTokens.Add("fake-token-CR-001#1");

        var output = await _app.RunAsync(["CR-001", "", "5", Number, "", "", "7"]);

        Assert.Contains("The server no longer accepts your sign-in.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("The status was not changed", output, StringComparison.Ordinal);
    }
}
