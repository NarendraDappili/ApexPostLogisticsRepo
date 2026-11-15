// Copilot-assisted: drafted by GitHub Copilot from Prompt 1, then reviewed and changed by hand. Each test's decision
// (kept, changed, rejected or added) and the reason is in COPILOT-LOG.md, section 2.
using System.Net;
using ApexPost.Courier;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Sync;
using ApexPost.UnitTests.Courier.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.UnitTests.Courier.Sync;

/// <summary>
/// Week 5 (S9), risks R3, R4 and R6: how the courier's uploader turns head office's answers into one outcome per scan.
/// It runs on a real <see cref="ApexApiClient"/> over a fake HTTP handler, so the rules are tested without the app
/// host or a network. The same uploader is tested through the app's composition by ApiShipmentUploaderTests in
/// ApexPost.Courier.IntegrationTests.
/// </summary>
public sealed class ApiShipmentUploaderTests
{
    private static readonly Uri Server = new("https://api.apexpost.example/");

    // Review: Copilot's answer here ended in "} }", which is not JSON. The client reads such an answer as unreadable
    // and gives no outcome, so the test failed before it could check the rule. The JSON was fixed; the assertions were kept.
    [Fact]
    public async Task UploadAsync_ScansForTwoParcels_SendsOneRequestPerParcel()
    {
        // Arrange
        var firstParcelScan = Scan("APX-30000001");
        var secondParcelScan = Scan("APX-30000002");
        var anotherFirstParcelScan = Scan("APX-30000001");
        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.OK,
            """{ "accepted": 1, "duplicatesSkipped": 0, "rejected": 0, "rejections": [], "shipmentStatus": "PickedUp" }""");

        // Act
        var outcomes = await CreateUploader(handler).UploadAsync(
            [firstParcelScan, secondParcelScan, anotherFirstParcelScan]);

        // Assert
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            [
                "https://api.apexpost.example/api/v2/shipments/APX-30000001/scans",
                "https://api.apexpost.example/api/v2/shipments/APX-30000002/scans",
            ],
            handler.Requests.Select(request => request.Uri!.ToString()));

        Assert.Equal(
            [firstParcelScan.Id.Value, anotherFirstParcelScan.Id.Value, secondParcelScan.Id.Value],
            outcomes.Select(outcome => outcome.ScanId.Value));

        Assert.All(outcomes, outcome =>
        {
            Assert.Equal(ScanUploadStatus.Accepted, outcome.Status);
            Assert.Null(outcome.Reason);
        });
    }

    // Review: kept. The same malformed JSON was fixed, although no request is sent here, so the body is never read.
    [Fact]
    public async Task UploadAsync_ScanBelongsToAnotherCourier_DoesNotSendItAndReturnsNoOutcome()
    {
        // Arrange
        var scan = Scan("APX-30000001", courierId: "CR-002");
        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.OK,
            """{ "accepted": 1, "duplicatesSkipped": 0, "rejected": 0, "rejections": [], "shipmentStatus": "PickedUp" }""");

        // Act
        var outcomes = await CreateUploader(handler).UploadAsync([scan]);

        // Assert
        Assert.Empty(handler.Requests);
        Assert.Empty(outcomes);
    }

    // Review: kept. Only the raw string was fixed: a multi-line raw string must start on a new line, so the draft did not compile.
    [Fact]
    public async Task UploadAsync_ServerListsOneScanInRejections_RejectsThatScanAndAcceptsTheOthers()
    {
        // Arrange
        var acceptedScan = Scan("APX-30000001");
        var rejectedScan = Scan("APX-30000001", ScanType.Delivered);
        const string reason = "A Delivered scan needs the shipment to be out for delivery.";

        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.OK,
            $$"""
            {
                "accepted": 1,
                "duplicatesSkipped": 0,
                "rejected": 1,
                "rejections": [
                    { "scanId": "{{rejectedScan.Id.Value}}", "reason": "{{reason}}" }
                ],
                "shipmentStatus": "PickedUp"
            }
            """);

        // Act
        var outcomes = await CreateUploader(handler).UploadAsync([acceptedScan, rejectedScan]);

        // Assert
        Assert.Equal(
            [
                new ScanUploadOutcome(acceptedScan.Id, ScanUploadStatus.Accepted),
                new ScanUploadOutcome(rejectedScan.Id, ScanUploadStatus.Rejected, reason),
            ],
            outcomes);
    }

    // Review: kept. Only the raw string was fixed.
    [Fact]
    public async Task UploadAsync_ServerReturns404_RejectsEveryScanForThatParcelWithTheServerMessage()
    {
        // Arrange
        var firstScan = Scan("APX-39999999");
        var secondScan = Scan("APX-39999999", ScanType.Delivered);
        const string message = "No shipment has the tracking number APX-39999999.";

        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.NotFound,
            $$"""
            {
                "title": "Not Found",
                "status": 404,
                "detail": "{{message}}"
            }
            """,
            "application/problem+json");

        // Act
        var outcomes = await CreateUploader(handler).UploadAsync([firstScan, secondScan]);

        // Assert
        Assert.Equal(
            [
                new ScanUploadOutcome(firstScan.Id, ScanUploadStatus.Rejected, message),
                new ScanUploadOutcome(secondScan.Id, ScanUploadStatus.Rejected, message),
            ],
            outcomes);
    }

    // Review: kept unchanged.
    [Fact]
    public async Task UploadAsync_BadRequestNamesSecondScan_RejectsOnlyTheNamedScan()
    {
        // Arrange
        var firstScan = Scan("APX-30000001");
        var secondScan = Scan("APX-30000001", ScanType.InTransit);
        var thirdScan = Scan("APX-30000001");

        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.BadRequest,
            """
            {
                "title": "One or more validation errors occurred.",
                "status": 400,
                "errors": {
                    "scans[1].scannedAtUtc": [
                        "scannedAtUtc cannot be in the future."
                    ]
                }
            }
            """,
            "application/problem+json");

        // Act
        var outcomes = await CreateUploader(handler).UploadAsync([firstScan, secondScan, thirdScan]);

        // Assert
        var outcome = Assert.Single(outcomes);
        Assert.Equal(secondScan.Id, outcome.ScanId);
        Assert.Equal(ScanUploadStatus.Rejected, outcome.Status);
        Assert.Equal(
            "scannedAtUtc: scannedAtUtc cannot be in the future.",
            outcome.Reason);
    }

    // Review: added by hand. Copilot did not cover the 400 that names no scan: then the whole request was refused,
    // and every scan in it is rejected with the server's message.
    [Fact]
    public async Task UploadAsync_BadRequestNamingNoScan_RejectsEveryScanWithTheMessage()
    {
        // Arrange
        var firstScan = Scan("APX-30000001");
        var secondScan = Scan("APX-30000001", ScanType.InTransit);
        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.BadRequest,
            """{ "title": "One or more validation errors occurred.", "status": 400, "errors": { "scans": ["An upload holds 1 to 100 scans."] } }""",
            "application/problem+json");

        // Act
        var outcomes = await CreateUploader(handler).UploadAsync([firstScan, secondScan]);

        // Assert
        const string message = "The server did not accept the data: scans: An upload holds 1 to 100 scans.";
        Assert.Equal(
            [
                new ScanUploadOutcome(firstScan.Id, ScanUploadStatus.Rejected, message),
                new ScanUploadOutcome(secondScan.Id, ScanUploadStatus.Rejected, message),
            ],
            outcomes);
    }

    // Review: rejected and rewritten. Copilot's version only checked that nothing was thrown. Its "no outcome" check
    // ran a second uploader on an empty list, so it could never fail and said nothing about the call under test.
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "The courier is not allowed to upload these scans.")]
    [InlineData(HttpStatusCode.Conflict, "The scans conflict with another upload.")]
    public async Task UploadAsync_NonTerminalServerStatus_ReturnsNoOutcomeAndDoesNotThrow(
        HttpStatusCode statusCode,
        string message)
    {
        // Arrange
        var scan = Scan("APX-30000001");
        var handler = FakeHttpMessageHandler.Answering(
            statusCode,
            $$"""{ "title": "Upload rejected", "status": {{(int)statusCode}}, "detail": "{{message}}" }""",
            "application/problem+json");

        // Act
        var outcomes = await CreateUploader(handler).UploadAsync([scan]);

        // Assert: the scan was sent once and has no outcome, so the outbox keeps it Pending for a later sync.
        Assert.Single(handler.Requests);
        Assert.Empty(outcomes);
    }

    // Review: added by hand, from a finding. Copilot's broken JSON in the first test reached a path it never tested on
    // purpose: a success whose body cannot be read gives no outcome, so the scans stay Pending rather than being
    // marked Synced on an answer nobody could read.
    [Fact]
    public async Task UploadAsync_SuccessAnswerThatCannotBeRead_GivesNoOutcome()
    {
        // Arrange
        var scan = Scan("APX-30000001");
        var handler = FakeHttpMessageHandler.Answering(HttpStatusCode.OK, "<html>Welcome to the hotel Wi-Fi</html>", "text/html");

        // Act
        var outcomes = await CreateUploader(handler).UploadAsync([scan]);

        // Assert
        Assert.Single(handler.Requests);
        Assert.Empty(outcomes);
    }

    // Review: changed. Copilot expected the exception message to be the server's detail. The client deliberately
    // rewrites it for the courier ("Please sign in again. ...", and never server internals for a 5xx), so both rows
    // failed for a wrong expected value. That rewriting is tested in ApexApiClientHttpTests; this test now checks
    // what the uploader decides: it throws, and the second parcel is never sent.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "The access token is no longer valid.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "The server is temporarily unavailable.")]
    public async Task UploadAsync_TerminalServerStatus_ThrowsAndStopsTheBatch(
        HttpStatusCode statusCode,
        string message)
    {
        // Arrange
        var firstScan = Scan("APX-30000001");
        var secondScan = Scan("APX-30000002");
        var handler = FakeHttpMessageHandler.Answering(
            statusCode,
            $$"""{ "title": "Upload failed", "status": {{(int)statusCode}}, "detail": "{{message}}" }""",
            "application/problem+json");

        // Act
        var exception = await Assert.ThrowsAsync<ApexApiException>(
            () => CreateUploader(handler).UploadAsync([firstScan, secondScan]));

        // Assert
        Assert.Equal(statusCode, exception.StatusCode);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.apexpost.example/api/v2/shipments/APX-30000001/scans", request.Uri!.ToString());
    }

    // Review: Copilot put "// Arrange" and "// Act" comments in this helper; they belong to tests, so they were removed.
    private static ApiShipmentUploader CreateUploader(FakeHttpMessageHandler handler)
    {
        var api = new ApexApiClient(new HttpClient(handler) { BaseAddress = Server });
        var session = new CourierSession();
        session.SignIn("CR-001");
        return new ApiShipmentUploader(api, session, NullLogger<ApiShipmentUploader>.Instance);
    }

    private static ShipmentScan Scan(
        string trackingNumber,
        ScanType scanType = ScanType.Pickup,
        string courierId = "CR-001") =>
        ShipmentScan.Record(
            trackingNumber,
            scanType,
            courierId,
            "Riverside Depot",
            TestTime.Now.AddMinutes(-5));
}
