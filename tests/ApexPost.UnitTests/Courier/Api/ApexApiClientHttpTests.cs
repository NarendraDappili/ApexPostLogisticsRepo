using System.Net;
using System.Text.Json;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.UnitTests.Courier.Api;

/// <summary>
/// Week 5 (S6), risk R6: the courier's typed API client (Week 4 Part D) on a fake <see cref="HttpMessageHandler"/>.
/// The client is built directly on <c>new HttpClient(handler)</c>, with no host, no store, no resilience pipeline and
/// no network. Each answer a server can give is turned into what the courier app relies on: a parsed result, an
/// <see cref="ApexApiException"/> with a message fit for the courier, or an <see cref="ApiUnavailableException"/>.
/// <para>
/// What this does not prove: the retry, circuit breaker and timeout policy, or the token handler. They are part of
/// the client's registration, so ResilienceTests and TokenHandlingTests (ApexPost.Courier.IntegrationTests) test them
/// through the app's real composition. It also does not prove the server's real JSON: the contract tests do that.
/// </para>
/// </summary>
public sealed class ApexApiClientHttpTests
{
    private static readonly Uri Server = new("https://api.apexpost.example/");

    [Fact]
    public async Task UploadScansAsync_200WithJson_PostsTheScansAndReturnsTheParsedResult()
    {
        // Arrange
        var scan = Scan();
        var handler = FakeHttpMessageHandler.Answering(HttpStatusCode.OK,
            """{ "accepted": 1, "duplicatesSkipped": 0, "rejected": 0, "rejections": [], "shipmentStatus": "PickedUp" }""");

        // Act
        var result = await Client(handler).UploadScansAsync("APX-30000001", [scan]);

        // Assert
        Assert.Equal((1, 0, 0, "PickedUp"), (result.Accepted, result.DuplicatesSkipped, result.Rejected, result.ShipmentStatus));
        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "https://api.apexpost.example/api/v2/shipments/APX-30000001/scans"), (request.Method, request.Uri!.ToString()));
        using var body = JsonDocument.Parse(request.Body!);
        var sent = Assert.Single(body.RootElement.GetProperty("scans").EnumerateArray());
        Assert.Equal((scan.Id.ToString(), "Pickup"), (sent.GetProperty("scanId").GetString(), sent.GetProperty("scanType").GetString()));
    }

    [Fact]
    public async Task UploadScansAsync_404ProblemDetails_ThrowsApiExceptionWithTheServersDetail()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Answering(HttpStatusCode.NotFound,
            """{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Not Found", "status": 404, "detail": "No shipment has the tracking number APX-39999999.", "traceId": "00-404" }""",
            "application/problem+json");

        // Act
        var exception = await Assert.ThrowsAsync<ApexApiException>(() => Client(handler).UploadScansAsync("APX-39999999", [Scan()]));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
        Assert.Equal("No shipment has the tracking number APX-39999999.", exception.Message);
        Assert.Equal(("Not Found", 404), (exception.Problem!.Title, exception.Problem.Status));
    }

    [Fact]
    public async Task UploadScansAsync_500WithServerInternals_ThrowsWithOnlyTheReferenceAndNeverTheInternals()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Answering(HttpStatusCode.InternalServerError,
            """{ "title": "An error occurred", "status": 500, "detail": "Microsoft.Data.SqlClient.SqlException: Invalid object name 'TrackingScans'. at ApexPost.Infrastructure...", "traceId": "00-ref-500" }""",
            "application/problem+json");

        // Act
        var exception = await Assert.ThrowsAsync<ApexApiException>(() => Client(handler).UploadScansAsync("APX-30000001", [Scan()]));

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal("The server had a problem (reference 00-ref-500). Your scans are saved on this device and will be sent on the next sync.", exception.Message);
        Assert.DoesNotContain("SqlException", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadScansAsync_400ValidationProblem_MessageNamesEachRefusedField()
    {
        // Arrange
        var handler = FakeHttpMessageHandler.Answering(HttpStatusCode.BadRequest,
            """{ "title": "One or more validation errors occurred.", "status": 400, "errors": { "scans[0].scannedAtUtc": ["scannedAtUtc cannot be in the future."], "scans[0].location": ["location is required."] } }""",
            "application/problem+json");

        // Act
        var exception = await Assert.ThrowsAsync<ApexApiException>(() => Client(handler).UploadScansAsync("APX-30000001", [Scan()]));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.StartsWith("The server did not accept the data: ", exception.Message, StringComparison.Ordinal);
        Assert.Contains("scans[0].location: location is required.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("scans[0].scannedAtUtc: scannedAtUtc cannot be in the future.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadScansAsync_ServerNeverAnswersWithinTheTimeout_ThrowsUnavailableTimedOut()
    {
        // Arrange: the handler waits until it is cancelled; the client gives up after 100 ms.
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        var client = Client(handler, timeout: TimeSpan.FromMilliseconds(100));

        // Act
        var exception = await Assert.ThrowsAsync<ApiUnavailableException>(() => client.UploadScansAsync("APX-30000001", [Scan()]));

        // Assert
        Assert.Equal(UnavailableReason.TimedOut, exception.Reason);
    }

    [Fact]
    public async Task UploadScansAsync_CallerCancelsDuringTheCall_ThrowsOperationCanceledNotAServerError()
    {
        // Arrange: the courier leaves the screen while the request is on its way.
        using var leaving = new CancellationTokenSource();
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await leaving.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });

        // Act
        var exception = await Record.ExceptionAsync(() => Client(handler).UploadScansAsync("APX-30000001", [Scan()], leaving.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.IsNotType<ApiUnavailableException>(exception);
    }

    [Fact]
    public async Task UploadScansAsync_NoConnection_ThrowsUnavailableNoConnection()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("No such host is known."));

        // Act
        var exception = await Assert.ThrowsAsync<ApiUnavailableException>(() => Client(handler).UploadScansAsync("APX-30000001", [Scan()]));

        // Assert
        Assert.Equal(UnavailableReason.NoConnection, exception.Reason);
    }

    private static ApexApiClient Client(HttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler) { BaseAddress = Server, Timeout = timeout ?? TimeSpan.FromSeconds(30) });

    private static ShipmentScan Scan() =>
        ShipmentScan.Record("APX-30000001", ScanType.Pickup, "CR-001", "Riverside Depot", TestTime.Now.AddMinutes(-5));
}
