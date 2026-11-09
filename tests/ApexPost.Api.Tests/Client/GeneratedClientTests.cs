using ApexPost.Api.Tests.Hosting;
using ApexPost.Client;
using ApexPost.Client.Consumer;

namespace ApexPost.Api.Tests.Client;

/// <summary>
/// C6: the client generated from contracts/v2.json, and the console consumer, against the real API on a disposable
/// database. Errors arrive as the generated exception types, carrying the API's ProblemDetails.
/// </summary>
[Collection(SqlApiCollection.Name)]
public sealed class GeneratedClientTests(SqlApiFactory factory)
{
    private readonly ApexPostClient _client = new(factory.CreateClient());

    [Fact]
    public async Task The_console_tour_creates_fetches_uploads_two_scans_and_handles_a_404()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        var output = new StringWriter();

        var exitCode = await new ShipmentTour(_client, TimeProvider.System).RunAsync(output, number);

        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.True(exitCode == 0, output.ToString());
        Assert.Collection(lines,
            line => Assert.Equal($"Created {number}: Created, 1.75 kg, for Kofi Boateng in Portside.", line),
            line => Assert.Equal($"Fetched {number}: Created, latest scan: none yet.", line),
            line => Assert.Equal("Uploaded 2 scans: 2 accepted, 0 duplicates skipped, 0 rejected; the shipment is now InTransit.", line),
            line => Assert.StartsWith($"Fetched {number}: InTransit, latest scan: InTransit at Northgate hub, ", line),
            line => Assert.Equal($"Not found, as expected (404): The requested record was not found. No shipment has the tracking number {ShipmentTour.UnknownTrackingNumber}.", line));
        Assert.Equal([TrackingScanType.Pickup, TrackingScanType.InTransit], (await _client.GetScanHistoryAsync(number)).Select(scan => scan.ScanType));
    }

    [Fact]
    public async Task A_404_is_the_generated_exception_with_the_problem_details()
    {
        var exception = await Assert.ThrowsAsync<ApexPostApiException<ProblemDetails>>(() => _client.GetShipmentAsync(SqlApiFactory.NextTrackingNumber()));

        Assert.Equal((404, true), (exception.StatusCode, exception.IsNotFound));
        Assert.Equal((404, "The requested record was not found."), (exception.Result.Status, exception.Result.Title));
    }

    [Fact]
    public async Task A_400_is_the_generated_exception_with_the_validation_errors_by_field()
    {
        var request = NewShipment("APX-1234");

        var exception = await Assert.ThrowsAsync<ApexPostApiException<ValidationProblemDetails>>(() => _client.CreateShipmentAsync(request));

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("trackingNumber", exception.Result.Errors!.Keys);
    }

    [Fact]
    public async Task A_409_is_the_generated_exception_with_the_problem_details()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        await _client.CreateShipmentAsync(NewShipment(number));

        var exception = await Assert.ThrowsAsync<ApexPostApiException<ProblemDetails>>(() => _client.CreateShipmentAsync(NewShipment(number)));

        Assert.Equal((409, true), (exception.StatusCode, exception.IsConflict));
        Assert.Equal("The request conflicts with the current data.", exception.Result.Title);
    }

    [Fact]
    public async Task Try_get_turns_only_the_404_into_null()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        await _client.CreateShipmentAsync(NewShipment(number));

        Assert.Equal(number, (await _client.TryGetShipmentAsync(number))?.TrackingNumber);
        Assert.Null(await _client.TryGetShipmentAsync(SqlApiFactory.NextTrackingNumber()));
    }

    [Fact]
    public async Task Enums_query_parameters_nested_objects_and_decimal_weights_round_trip()
    {
        var number = SqlApiFactory.NextTrackingNumber();
        var city = $"ClientCity{number[^6..]}";
        await _client.CreateShipmentAsync(NewShipment(number, city, 69.5m));
        await _client.UpdateShipmentStatusAsync(number, new UpdateStatusRequest { Status = TrackingStatus.PickedUp });

        var page = await _client.ListShipmentsAsync(page: 1, pageSize: 5, status: TrackingStatus.PickedUp, city: city);

        var shipment = Assert.Single(page.Items);
        Assert.Equal((number, TrackingStatus.PickedUp, 69.5m, "Ama Mensah", city), (shipment.TrackingNumber, shipment.Status, shipment.WeightKg, shipment.Recipient.Name, shipment.Recipient.City));
        Assert.Null(shipment.LatestScan);
        Assert.Empty((await _client.ListShipmentsAsync(status: TrackingStatus.Delivered, city: city)).Items);
    }

    private static CreateShipmentRequest NewShipment(string trackingNumber, string city = "Portside", decimal weightKg = 5m) => new()
    {
        TrackingNumber = trackingNumber,
        RecipientName = "Ama Mensah",
        RecipientPhone = "+44 20 7946 0000",
        RecipientAddress = "12 Harbour Road",
        City = city,
        WeightKg = weightKg,
    };
}
