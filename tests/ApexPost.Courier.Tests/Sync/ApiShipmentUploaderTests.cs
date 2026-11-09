using System.Net;
using System.Text.Json.Nodes;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApexPost.Courier.Tests.Sync;

/// <summary>
/// D9: the real uploader that replaced Part A's stand-in. It sends the outbox's scans to the v2 API (the in-memory
/// FakeApi, which follows the API's rules), one request per shipment, and turns each answer into one outcome per
/// scan: what the outbox then marks Synced or Failed.
/// </summary>
public sealed class ApiShipmentUploaderTests : IDisposable
{
    private readonly CourierAppRun _app = new();
    private readonly IHost _host;

    public ApiShipmentUploaderTests()
    {
        _host = _app.CreateHost(TextReader.Null, TextWriter.Null);
        SignIn("CR-001");
    }

    public void Dispose()
    {
        _host.Dispose();
        _app.Dispose();
    }

    private IShipmentUploader Uploader => _host.Services.GetRequiredService<IShipmentUploader>();

    [Fact]
    public async Task Scans_go_one_request_per_shipment_and_each_stored_scan_is_accepted()
    {
        ShipmentScan[] scans = [Scan("APX-30000001"), Scan("APX-30000002"), Scan("APX-30000001")];

        var outcomes = await Uploader.UploadAsync(scans);

        Assert.Equal(["/api/v2/shipments/APX-30000001/scans", "/api/v2/shipments/APX-30000002/scans"], _app.Api.Server.Requests.Select(request => request.Uri.AbsolutePath));
        Assert.Equal(2, JsonNode.Parse(_app.Api.Server.Requests[0].Body!)!["scans"]!.AsArray().Count);
        Assert.All(outcomes, outcome => Assert.Equal(ScanUploadStatus.Accepted, outcome.Status));
        Assert.Equal(scans.Select(scan => scan.Id.Value).Order(), outcomes.Select(outcome => outcome.ScanId.Value).Order());
    }

    [Fact]
    public async Task A_scan_the_server_already_has_is_accepted_again()
    {
        var scan = Scan("APX-30000001");
        await Uploader.UploadAsync([scan]);

        var again = Assert.Single(await Uploader.UploadAsync([scan]));

        Assert.Equal(ScanUploadStatus.Accepted, again.Status);
        Assert.Single(_app.Api.Stored);
    }

    [Fact]
    public async Task A_scan_the_api_rejects_is_rejected_with_its_reason()
    {
        _app.Api.RejectWhen = scan => scan["scanType"]!.GetValue<string>() == "Delivered" ? "A Delivered scan needs the shipment to be out for delivery." : null;
        ShipmentScan[] scans = [Scan("APX-30000001"), Scan("APX-30000001", ScanType.Delivered)];

        var outcomes = await Uploader.UploadAsync(scans);

        Assert.Equal(ScanUploadStatus.Accepted, outcomes.Single(outcome => outcome.ScanId == scans[0].Id).Status);
        Assert.Equal((ScanUploadStatus.Rejected, "A Delivered scan needs the shipment to be out for delivery."),
            outcomes.Single(outcome => outcome.ScanId == scans[1].Id) is var rejected ? (rejected.Status, rejected.Reason) : default);
    }

    [Fact]
    public async Task An_unknown_shipment_rejects_its_scans_and_the_others_still_go()
    {
        _app.Api.UnknownShipments.Add("APX-39999999");
        ShipmentScan[] scans = [Scan("APX-39999999"), Scan("APX-30000001")];

        var outcomes = await Uploader.UploadAsync(scans);

        var rejected = outcomes.Single(outcome => outcome.ScanId == scans[0].Id);
        Assert.Equal((ScanUploadStatus.Rejected, "No shipment has the tracking number APX-39999999."), (rejected.Status, rejected.Reason));
        Assert.Equal(ScanUploadStatus.Accepted, outcomes.Single(outcome => outcome.ScanId == scans[1].Id).Status);
    }

    [Fact]
    public async Task A_400_rejects_only_the_scans_it_names_and_leaves_the_others_pending_to_go_alone_next_time()
    {
        ShipmentScan[] scans = [Scan("APX-30000001"), Scan("APX-30000001", scannedAt: DateTimeOffset.UtcNow.AddDays(1)), Scan("APX-30000001")];

        var outcomes = await Uploader.UploadAsync(scans);

        var rejected = Assert.Single(outcomes);
        Assert.Equal(scans[1].Id, rejected.ScanId);
        Assert.Equal(ScanUploadStatus.Rejected, rejected.Status);
        Assert.StartsWith("scannedAtUtc: scannedAtUtc cannot be in the future", rejected.Reason, StringComparison.Ordinal);

        var next = await Uploader.UploadAsync([scans[0], scans[2]]);
        Assert.All(next, outcome => Assert.Equal(ScanUploadStatus.Accepted, outcome.Status));
    }

    [Fact]
    public async Task Another_couriers_scans_are_not_sent_and_stay_pending()
    {
        var outcomes = await Uploader.UploadAsync([Scan("APX-30000001", courierId: "CR-002"), Scan("APX-30000002")]);

        Assert.Equal(["/api/v2/shipments/APX-30000002/scans"], _app.Api.Server.Requests.Select(request => request.Uri.AbsolutePath));
        Assert.Single(outcomes);
    }

    [Fact]
    public async Task A_403_leaves_the_scans_pending_and_does_not_stop_the_others()
    {
        // Signed in as CR-002 but holding CR-001's token: the API refuses CR-002's scans.
        _host.Services.GetRequiredService<CourierSession>().SignIn("CR-002");

        var outcomes = await Uploader.UploadAsync([Scan("APX-30000001", courierId: "CR-002")]);

        Assert.Empty(outcomes);
        Assert.Single(_app.Api.Server.Requests);
    }

    [Fact]
    public async Task Without_a_valid_sign_in_the_batch_stops_with_a_401()
    {
        await _host.Services.GetRequiredService<ITokenStore>().ClearAsync();

        var exception = await Assert.ThrowsAsync<ApexApiException>(() => Uploader.UploadAsync([Scan("APX-30000001")]));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task A_server_that_is_down_stops_the_batch_so_every_scan_stays_pending()
    {
        _app.Api.Down = true;

        await Assert.ThrowsAsync<ApexApiException>(() => Uploader.UploadAsync([Scan("APX-30000001"), Scan("APX-30000002")]));

        // Four tries of the first shipment, then the batch stopped: the second was never sent.
        Assert.All(_app.Api.Server.Requests, request => Assert.EndsWith("APX-30000001/scans", request.Uri.AbsolutePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_outbox_sends_45_pending_scans_in_batches_of_20_and_marks_each_synced()
    {
        var repository = _host.Services.GetRequiredService<IScanRepository>();
        for (var i = 0; i < 45; i++) repository.Create(Scan($"APX-3000000{i % 3}"));

        var result = await _host.Services.GetRequiredService<OutboxService>().SyncPendingAsync();

        Assert.Equal((45, 45, 0, 0), (result.Attempted, result.Synced, result.Failed, result.LeftPending));
        Assert.All(repository.ListAll(), scan => Assert.Equal(SyncStatus.Synced, scan.SyncStatus));
        Assert.Equal(45, _app.Api.Stored.Count);
        Assert.All(_app.Api.Server.Requests, request => Assert.InRange(JsonNode.Parse(request.Body!)!["scans"]!.AsArray().Count, 1, 20));
        // Three batches (20, 20, 5), each split over the three shipments.
        Assert.Equal(9, _app.Api.Server.Requests.Count);
    }

    private void SignIn(string courierId)
    {
        _host.Services.GetRequiredService<CourierSession>().SignIn(courierId);
        _host.Services.GetRequiredService<ITokenStore>().SaveAsync($"fake-token-{courierId}").AsTask().GetAwaiter().GetResult();
    }

    private static ShipmentScan Scan(string trackingNumber, ScanType type = ScanType.Pickup, string courierId = "CR-001", DateTimeOffset? scannedAt = null) =>
        ShipmentScan.Record(trackingNumber, type, courierId, "Riverside Depot", scannedAt ?? DateTimeOffset.UtcNow.AddMinutes(-10));
}
