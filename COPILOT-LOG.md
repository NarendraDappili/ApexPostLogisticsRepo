# Copilot log: AI-drafted tests and their review (Week 5, Task 6)

GitHub Copilot drafted two pieces of test code:
1. unit tests for one service;
2. one Gherkin feature with its step definitions.

Every draft was then reviewed by hand against the checklist below. This log records:
- each prompt, word for word;
- what Copilot produced;
- what was kept, changed or rejected, and why;
- two proofs that kept tests are meaningful: the production code was broken on purpose, the test failed, and the code was restored.

Copilot-assisted test classes carry a header comment pointing to their entry here.

| | |
|---|---|
| Tool | GitHub Copilot Free in Visual Studio 2026, Copilot Chat in **Ask** mode, model **GPT 5.6 Luna** (as shown in the model picker). One chat thread per prompt. |
| Repository instructions | `.github/copilot-instructions.md`, updated before drafting with the Week 5 test conventions (section 1) |
| Reviewer | the project author |

## 1. How Copilot was set up

Copilot reads `.github/copilot-instructions.md` with every request. Before drafting, its Week 2 text was brought up to date. It had said "JSON files for persistence" and "only approved libraries", which would have steered Copilot away from Moq, Reqnroll and `FakeTimeProvider`. It also gained a "Writing tests" section, which gives Copilot the same rules a human contributor gets:
- `Method_Scenario_ExpectedResult` names;
- Arrange, Act, Assert;
- `InlineData` rows with no random values;
- exact assertions;
- no system clock and no real network;
- Moq for interactions, never a mocked `DbContext`;
- Gherkin in business language.

The prompts below give Copilot the unit's rules in plain words and point to existing helpers with `#file` references. They do not paste in the expected answers. Whether Copilot's assertions match the real behaviour is exactly what the review checks.

## 2. Draft 1: unit tests for `ApiShipmentUploader`

**Why this service.** `ApiShipmentUploader` (courier core) turns the API's answers into one outcome per scan. The rules are easy to get subtly wrong:
- which courier's scans are sent at all;
- a rejection versus an acceptance in the same answer;
- a 404 rejecting every scan, while a 400 rejects only the scans it names;
- a 403 or 409 that leaves scans Pending;
- a 401 or 5xx that must stop the whole batch.

Today it is tested only at component level, through the app's composition with a fake API (`ApiShipmentUploaderTests` in `ApexPost.Courier.IntegrationTests`). A unit-level suite is a real addition, and a good test of whether generated tests understand the rules or only mirror the code.

The strategy first named `OutboxService`. Step S6 has since covered it with Moq interaction tests, so Copilot drafts for it would mostly duplicate those.

### Prompt 1 (as sent)

The `#file:` lines were attached with Visual Studio's `#` file picker; the rest was pasted as it stands.

```text
#file:src/ApexPost.Courier.Core/Sync/ApiShipmentUploader.cs
#file:tests/ApexPost.UnitTests/Courier/Api/FakeHttpMessageHandler.cs
#file:tests/ApexPost.UnitTests/Courier/Api/ApexApiClientHttpTests.cs

Write xUnit unit tests for ApiShipmentUploader. Put them in
tests/ApexPost.UnitTests/Courier/Sync/ApiShipmentUploaderTests.cs, namespace ApexPost.UnitTests.Courier.Sync.

Build the uploader on a real ApexApiClient over
new HttpClient(handler) { BaseAddress = new Uri("https://api.apexpost.example/") },
where handler is the existing FakeHttpMessageHandler. Sign the CourierSession in with SignIn("CR-001").
Use NullLogger<ApiShipmentUploader>.Instance. Create scans with
ShipmentScan.Record(trackingNumber, scanType, courierId, location, TestTime.Now.AddMinutes(-5)).

Cover these rules:
1. Scans of two parcels are sent as one request per parcel.
2. Scans of another courier are not sent and get no outcome.
3. A scan the server lists in "rejections" is Rejected with the server's reason; the other scans of that request are Accepted.
4. A 404 rejects every scan of that parcel, with the server's message.
5. A 400 whose errors name scans[1].scannedAtUtc rejects only the second scan; the other scans get no outcome.
6. A 403 or a 409 gives no outcome and does not throw.
7. A 401 or a 503 stops the batch: UploadAsync throws.

Follow the conventions in .github/copilot-instructions.md: Method_Scenario_ExpectedResult names, Arrange/Act/Assert
comments, a Theory where rows differ only in data, exact assertions on scan IDs, statuses and reasons.
```

### What Copilot produced

Copilot's reply began with one sentence and then the file:

> I inspected the uploader, fake handler, API client tests, domain types, and project conventions. Add this file as tests/ApexPost.UnitTests/Courier/Sync/ApiShipmentUploaderTests.cs:

<details><summary>The draft, unchanged (7 tests, 9 cases)</summary>

```csharp
using System.Net;
using ApexPost.Courier;
using ApexPost.Courier.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Sync;
using ApexPost.UnitTests.Courier.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.UnitTests.Courier.Sync;

public sealed class ApiShipmentUploaderTests
{
    private static readonly Uri Server = new("https://api.apexpost.example/");

    [Fact]
    public async Task UploadAsync_ScansForTwoParcels_SendsOneRequestPerParcel()
    {
        // Arrange
        var firstParcelScan = Scan("APX-30000001");
        var secondParcelScan = Scan("APX-30000002");
        var anotherFirstParcelScan = Scan("APX-30000001");
        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.OK,
            """{ "accepted": 1, "duplicatesSkipped": 0, "rejected": 0, "rejections": [], "shipmentStatus": "PickedUp" } }""");

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

    [Fact]
    public async Task UploadAsync_ScanBelongsToAnotherCourier_DoesNotSendItAndReturnsNoOutcome()
    {
        // Arrange
        var scan = Scan("APX-30000001", courierId: "CR-002");
        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.OK,
            """{ "accepted": 1, "duplicatesSkipped": 0, "rejected": 0, "rejections": [], "shipmentStatus": "PickedUp" } }""");

        // Act
        var outcomes = await CreateUploader(handler).UploadAsync([scan]);

        // Assert
        Assert.Empty(handler.Requests);
        Assert.Empty(outcomes);
    }

    [Fact]
    public async Task UploadAsync_ServerListsOneScanInRejections_RejectsThatScanAndAcceptsTheOthers()
    {
        // Arrange
        var acceptedScan = Scan("APX-30000001");
        var rejectedScan = Scan("APX-30000001", ScanType.Delivered);
        const string reason = "A Delivered scan needs the shipment to be out for delivery.";

        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.OK,
            $$"""{
                "accepted": 1,
                "duplicatesSkipped": 0,
                "rejected": 1,
                "rejections": [
                    { "scanId": "{{rejectedScan.Id.Value}}", "reason": "{{reason}}" }
                ],
                "shipmentStatus": "PickedUp"
            }""");

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

    [Fact]
    public async Task UploadAsync_ServerReturns404_RejectsEveryScanForThatParcelWithTheServerMessage()
    {
        // Arrange
        var firstScan = Scan("APX-39999999");
        var secondScan = Scan("APX-39999999", ScanType.Delivered);
        const string message = "No shipment has the tracking number APX-39999999.";

        var handler = FakeHttpMessageHandler.Answering(
            HttpStatusCode.NotFound,
            $$"""{
                "title": "Not Found",
                "status": 404,
                "detail": "{{message}}"
            }""",
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
            $$"""{
                "title": "Upload rejected",
                "status": {{(int)statusCode}},
                "detail": "{{message}}"
            }""",
            "application/problem+json");

        // Act
        var exception = await Record.ExceptionAsync(
            () => CreateUploader(handler).UploadAsync([scan]));

        // Assert
        Assert.Null(exception);
        Assert.Single(handler.Requests);
        Assert.Empty(await CreateUploader(
            FakeHttpMessageHandler.Answering(statusCode, $$"""{"status": {{(int)statusCode}}, "detail": "{{message}}"}""", "application/problem+json"))
            .UploadAsync([]));
    }

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
            $$"""{
                "title": "Upload failed",
                "status": {{(int)statusCode}},
                "detail": "{{message}}"
            }""",
            "application/problem+json");

        // Act
        var exception = await Assert.ThrowsAsync<ApexApiException>(
            () => CreateUploader(handler).UploadAsync([firstScan, secondScan]));

        // Assert
        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Equal(message, exception.Message);
        Assert.Single(handler.Requests);
        Assert.Equal(
            "https://api.apexpost.example/api/v2/shipments/APX-30000001/scans",
            handler.Requests[0].Uri!.ToString());
    }

    private static ApiShipmentUploader CreateUploader(FakeHttpMessageHandler handler)
    {
        // Arrange
        var api = new ApexApiClient(
            new HttpClient(handler)
            {
                BaseAddress = Server,
            });

        var session = new CourierSession();
        session.SignIn("CR-001");

        // Act
        return new ApiShipmentUploader(
            api,
            session,
            NullLogger<ApiShipmentUploader>.Instance);
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
```

</details>

### Review

**First run, as drafted: it did not compile.** Four tests build their JSON with multi-line raw strings that start on the same line as the opening quotes (`$$"""{`). C# requires a multi-line raw string to start on a new line, so the build stopped with CS8997, "Unterminated raw string literal".

**Second run, with only that syntax fixed:** 6 of 9 cases passed and 3 failed, all because of the draft, not the code under test:

```text
UploadAsync_ScansForTwoParcels_SendsOneRequestPerParcel [FAIL]
   Assert.Equal() Failure: Collections differ        (expected 3 outcomes, got none)
UploadAsync_TerminalServerStatus_ThrowsAndStopsTheBatch(statusCode: Unauthorized, ...) [FAIL]
   Assert.Equal() Failure: Strings differ   Expected: "The access token is no longer valid."
UploadAsync_TerminalServerStatus_ThrowsAndStopsTheBatch(statusCode: ServiceUnavailable, ...) [FAIL]
   Assert.Equal() Failure: Strings differ   Expected: "The server is temporarily unavailable."
Failed!  - Failed: 3, Passed: 6, Skipped: 0, Total: 9
```

**Decisions, one row per generated test.** The checklist numbers refer to section 4.

| Copilot's test | Decision | Why |
|---|---|---|
| `UploadAsync_ScansForTwoParcels_SendsOneRequestPerParcel` | **changed** | The server's answer ended in `} }`, which is not JSON (check 4, set-up). The client reads such an answer as unreadable and gives no outcome, so the test failed before reaching the rule. The JSON was fixed. The assertions were good (exact URLs, the order of the outcomes, the statuses) and were kept. |
| `UploadAsync_ScanBelongsToAnotherCourier_DoesNotSendItAndReturnsNoOutcome` | **kept** | It is right and exact: no request, no outcome. It had the same malformed JSON, which was harmless because no request is sent, and was fixed anyway. |
| `UploadAsync_ServerListsOneScanInRejections_RejectsThatScanAndAcceptsTheOthers` | **kept** (syntax only) | It compares whole outcome records, including the server's reason (check 3 passed). Only the raw string was fixed. |
| `UploadAsync_ServerReturns404_RejectsEveryScanForThatParcelWithTheServerMessage` | **kept** (syntax only) | It checks both scans and the message the courier sees. Only the raw string was fixed. |
| `UploadAsync_BadRequestNamesSecondScan_RejectsOnlyTheNamedScan` | **kept unchanged** | It checks that exactly one outcome exists, its scan ID, its status and the field-prefixed reason. |
| `UploadAsync_NonTerminalServerStatus_ReturnsNoOutcomeAndDoesNotThrow` (403, 409) | **rejected, rewritten** | It never checked the outcome of the call under test, only that nothing was thrown. Its "no outcome" assertion ran a second uploader on an empty list, which can never fail (checks 1 and 3: a test that cannot fail). The rewrite asserts that the real call sent one request and returned no outcome, so the outbox keeps the scan Pending. |
| `UploadAsync_TerminalServerStatus_ThrowsAndStopsTheBatch` (401, 503) | **changed** | A wrong expected value (check 2). Copilot assumed the exception message is the server's `detail`. The client deliberately rewrites it for the courier ("Please sign in again. …", and only a reference for a 5xx), so both rows failed. That rewriting belongs to `ApexApiClientHttpTests`. This test now checks what the uploader decides: it throws, and the second parcel is never sent. |
| the `CreateUploader` helper | **changed** | It carried `// Arrange` and `// Act` comments, which belong to tests, not helpers (check 7). They were removed. |

**Missing cases, added by hand (check 5):**
- `UploadAsync_BadRequestNamingNoScan_RejectsEveryScanWithTheMessage`: a 400 that names no scan means the whole request was refused, so every scan is rejected. The uploader has a branch for this, and Copilot did not test it.
- `UploadAsync_SuccessAnswerThatCannotBeRead_GivesNoOutcome`: Copilot's broken JSON reached this path by accident, which showed it had no deliberate test. An unreadable "success" must leave the scans Pending, never mark them Synced.

**Result:** 11 cases, all passing. Of Copilot's 7 tests, 4 were kept (2 of them with only the syntax fixed), 2 were changed, and 1 was rejected and rewritten; 2 were added by hand. The file carries a "Copilot-assisted" header, and a `// Review:` comment above each test says what happened to it.

## 3. Draft 2: a Gherkin feature with its step definitions

**Why this feature.** Changing a shipment's status at head office is a business promise in its own right:
- one step at a time;
- a refused move names the status allowed next;
- Delivered and Failed are final;
- only couriers may move a shipment.

The two hand-written features do not cover it. It reuses the existing support code (`HeadOfficeDriver`, `ScenarioState`, the hooks), so the review can check whether Copilot uses context injection and the driver, or invents static state and raw HTTP in the steps.

### Prompt 2 (as sent)

The `#file:` lines were attached with Visual Studio's `#` file picker; the rest was pasted as it stands.

```text
#file:tests/ApexPost.Specs/Features/ShipmentCreation.feature
#file:tests/ApexPost.Specs/Steps/ShipmentCreationSteps.cs
#file:tests/ApexPost.Specs/Drivers/HeadOfficeDriver.cs
#file:tests/ApexPost.Specs/Support/Contexts.cs

Write a Reqnroll feature and its step definitions for changing a shipment's status at head office.
Files: tests/ApexPost.Specs/Features/ShipmentStatusChange.feature and
tests/ApexPost.Specs/Steps/ShipmentStatusChangeSteps.cs (namespace ApexPost.Specs.Steps).

Business rules. The API is PATCH api/v2/shipments/{trackingNumber}/status with body { "status": "<status>" },
and it needs the Courier role.
- The status moves Created -> PickedUp -> InTransit -> OutForDelivery -> Delivered or Failed, one step at a time.
- A move that skips a step or goes back is refused, and the answer names the status that is allowed next.
- Delivered and Failed are final: the shipment's status cannot change any more.
- Staff without the Courier role cannot change a status.

Use the existing HeadOfficeDriver (Courier(courierId), Administrator(), RegisterAtAsync(trackingNumber, status),
NextTrackingNumber(), LookUpAsync) and ScenarioState, received by constructor injection; no static fields.
Write the feature in business language (no HTTP verbs, status codes or JSON), with a Background and at least one
Scenario Outline with Examples. Use anchored regular expressions in the step attributes, like ShipmentCreationSteps.cs.
```

### What Copilot produced

Copilot gave two files.

<details><summary>ShipmentStatusChange.feature, unchanged (4 scenarios, 11 cases)</summary>

```gherkin
Feature: Changing a shipment status at head office
  A shipment progresses through its delivery journey one step at a time.
  Only staff with the Courier role may change its status.

  Background:
    Given an operations administrator is signed in at head office

  Scenario Outline: A courier moves a shipment to the next status
    Given shipment "<trackingNumber>" is at status "<currentStatus>"
    And courier "CR-001" is signed in at head office
    When the courier changes shipment "<trackingNumber>" to status "<nextStatus>"
    Then shipment "<trackingNumber>" has status "<nextStatus>"

    Examples:
      | trackingNumber | currentStatus  | nextStatus     |
      | APX-53000001   | Created        | PickedUp       |
      | APX-53000002   | PickedUp       | InTransit      |
      | APX-53000003   | InTransit      | OutForDelivery |
      | APX-53000004   | OutForDelivery | Delivered      |
      | APX-53000005   | OutForDelivery | Failed         |

  Scenario Outline: A courier cannot skip or reverse a shipment status
    Given shipment "<trackingNumber>" is at status "<currentStatus>"
    And courier "CR-001" is signed in at head office
    When the courier changes shipment "<trackingNumber>" to status "<attemptedStatus>"
    Then the change is refused and the next allowed status is "<allowedStatus>"
    And shipment "<trackingNumber>" remains at status "<currentStatus>"

    Examples:
      | trackingNumber | currentStatus  | attemptedStatus | allowedStatus   |
      | APX-53000006   | Created        | InTransit       | PickedUp        |
      | APX-53000007   | InTransit      | Created         | OutForDelivery  |
      | APX-53000008   | OutForDelivery | PickedUp        | Delivered       |

  Scenario Outline: A final shipment status cannot be changed
    Given shipment "<trackingNumber>" is ready for its final status
    And courier "CR-001" is signed in at head office
    When the courier changes shipment "<trackingNumber>" to status "<finalStatus>"
    And the courier changes shipment "<trackingNumber>" to status "InTransit"
    Then the final status change is refused
    And shipment "<trackingNumber>" remains at status "<finalStatus>"

    Examples:
      | trackingNumber | finalStatus |
      | APX-53000009   | Delivered   |
      | APX-53000010   | Failed      |

  Scenario: Staff without the Courier role cannot change a shipment status
    Given shipment "APX-53000011" is at status "Created"
    When the administrator changes shipment "APX-53000011" to status "PickedUp"
    Then the status change is refused because the staff member is not a courier
    And shipment "APX-53000011" remains at status "Created"
```

</details>

<details><summary>ShipmentStatusChangeSteps.cs, unchanged</summary>

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Specs.Drivers;
using ApexPost.Specs.Support;

namespace ApexPost.Specs.Steps;

[Binding]
public sealed class ShipmentStatusChangeSteps(HeadOfficeDriver headOffice, ScenarioState state)
{
    private HttpClient SignedIn =>
        state.Client ?? throw new InvalidOperationException(
            "Nobody is signed in at head office yet.");

    [Given(@"^an operations administrator is signed in at head office$")]
    public void GivenAnOperationsAdministratorIsSignedIn() =>
        state.Client = headOffice.Administrator();

    [Given(@"^courier ""(.*)"" is signed in at head office$")]
    public void GivenACourierIsSignedIn(string courierId) =>
        state.Client = headOffice.Courier(courierId);

    [Given(@"^shipment ""(.*)"" is at status ""(.*)""$")]
    public async Task GivenShipmentIsAtStatus(string trackingNumber, string status)
    {
        state.TrackingNumber = trackingNumber;
        await headOffice.RegisterAtAsync(trackingNumber, status);
    }

    [Given(@"^shipment ""(.*)"" is ready for its final status$")]
    public async Task GivenShipmentIsReadyForItsFinalStatus(string trackingNumber)
    {
        state.TrackingNumber = trackingNumber;
        await headOffice.RegisterAtAsync(trackingNumber, "OutForDelivery");
    }

    [When(@"^the courier changes shipment ""(.*)"" to status ""(.*)""$")]
    public async Task WhenTheCourierChangesShipmentStatus(
        string trackingNumber,
        string status)
    {
        state.TrackingNumber = trackingNumber;
        state.LastResponse = await SignedIn.PatchAsJsonAsync(
            $"api/v2/shipments/{trackingNumber}/status",
            new { status });
    }

    [When(@"^the administrator changes shipment ""(.*)"" to status ""(.*)""$")]
    public async Task WhenTheAdministratorChangesShipmentStatus(
        string trackingNumber,
        string status)
    {
        state.TrackingNumber = trackingNumber;
        state.LastResponse = await SignedIn.PatchAsJsonAsync(
            $"api/v2/shipments/{trackingNumber}/status",
            new { status });
    }

    [Then(@"^shipment ""(.*)"" has status ""(.*)""$")]
    public async Task ThenShipmentHasStatus(string trackingNumber, string expectedStatus)
    {
        var shipment = await headOffice.LookUpAsync(trackingNumber);

        Assert.True(shipment.HasValue);
        Assert.Equal(
            expectedStatus,
            shipment.Value.GetProperty("status").GetString());
    }

    [Then(@"^the change is refused and the next allowed status is ""(.*)""$")]
    public async Task ThenTheChangeIsRefusedAndNextStatusIs(string allowedStatus)
    {
        var response = LastResponse();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = body.GetProperty("detail").GetString();

        Assert.Contains(allowedStatus, detail, StringComparison.Ordinal);
    }

    [Then(@"^the final status change is refused$")]
    public void ThenTheFinalStatusChangeIsRefused() =>
        Assert.Equal(HttpStatusCode.BadRequest, LastResponse().StatusCode);

    [Then(@"^the status change is refused because the staff member is not a courier$")]
    public void ThenTheStatusChangeIsRefusedForNonCourier() =>
        Assert.Equal(HttpStatusCode.Forbidden, LastResponse().StatusCode);

    [Then(@"^shipment ""(.*)"" remains at status ""(.*)""$")]
    public async Task ThenShipmentRemainsAtStatus(
        string trackingNumber,
        string expectedStatus)
    {
        var shipment = await headOffice.LookUpAsync(trackingNumber);

        Assert.True(shipment.HasValue);
        Assert.Equal(
            expectedStatus,
            shipment.Value.GetProperty("status").GetString());
    }

    private HttpResponseMessage LastResponse() =>
        state.LastResponse ?? throw new InvalidOperationException(
            "No status change was attempted in this scenario.");
}
```

</details>

### Review

**First run, as drafted: the build succeeded, but it broke the existing feature.**

```text
Failed!  - Failed: 18, Passed: 5, Skipped: 0, Total: 23
Reqnroll.AmbiguousBindingException : Ambiguous step definitions found for step
  'Given an operations administrator is signed in at head office': ...   (18 times)
```

The step class redefined "an operations administrator is signed in at head office" and "courier … is signed in at head office". Both already exist in `ShipmentCreationSteps`, and Reqnroll shares step bindings across the whole project. So every scenario using either step failed, **including all 7 hand-written ShipmentCreation scenarios**, which had passed before. Only the 5 ScanSync scenarios, which do not use those steps, survived.

**Second run, with only the duplicates removed:**

```text
Failed!  - Failed: 5, Passed: 18, Skipped: 0, Total: 23
   Expected: BadRequest
   Actual:   Conflict        (5 times: the 3 "skip or reverse" rows and the 2 "final status" rows)
```

**Decisions.** The checklist numbers refer to section 4.

| Copilot's part | Decision | Why |
|---|---|---|
| Duplicate "signed in at head office" bindings | **rejected** | They broke 18 scenarios across two features (check 4, set-up). The existing bindings are reused. |
| Expected `400 Bad Request` for a refused move and for a final status | **changed** | A wrong expected value (check 2). A refused move is a valid request that conflicts with the shipment's current state, which the API answers with `409 Conflict` (ADR 0026). |
| "the final status change is refused": status code only | **changed** | A weak assertion (check 3). It now also checks the reason the courier is given ("… and its status cannot change."), and the step was renamed "the change is refused because the status is final". |
| "next allowed status" with `Assert.Contains` and the example `Delivered` | **changed** | Too loose (check 3): from OutForDelivery the API allows "Delivered or Failed", and "Delivered" alone passed by being a substring. The example now states the whole rule, and the step checks the sentence's end exactly. |
| Raw `PATCH` calls written in the step class | **changed** | The prompt asked for the driver (check 7). `HeadOfficeDriver` gained `ChangeStatusAsync`, so the HTTP shape lives in one place. |
| Two identical "changes shipment … to status" methods (courier, administrator) and two identical status checks ("has" / "remains at") | **changed** | Duplicated logic (check 7). One binding with an alternation covers both actors, and one method carries both phrasings. |
| The final-status scenario: Given "ready for its final status", then two When steps | **changed** | Its first When was really set-up, so the scenario had two actions. It now starts from `Given shipment … is at status "Delivered"` (or "Failed"). The driver was extended to reach the final statuses for this. |
| Background "an operations administrator is signed in" | **changed** | The administrator acted in only one of the four scenarios, and the other three signed in a courier straight away. The Background now signs in the courier, and the one administrator scenario signs in its administrator explicitly. |
| The "next status" outline (5 rows), the "skip or reverse" rows, the courier-role scenario, `Given shipment … is at status …`, constructor injection of `HeadOfficeDriver` and `ScenarioState`, anchored regular expressions | **kept** | They are correct, in business language and to the conventions. The rows are a good choice: every allowed move, and one backward, one skipping and one from OutForDelivery. |

**Result:** 11 scenario cases, all passing, and the 12 hand-written scenarios pass again (23 of 23). Both files carry a "Copilot-assisted" header, and each step carries a `// Review:` comment.

## 4. Review checklist (applied to every generated test)

| # | Check | Typical failure in generated tests |
|---|---|---|
| 1 | **Does it test a rule, or mirror the code?** | the expected value is computed with the same logic as the implementation, so the test can never disagree with it |
| 2 | **Are the expected values right?** | a guessed status or message, for example expecting a 409 to throw or a 404 to leave scans Pending |
| 3 | **Are the assertions strong enough?** | `Assert.NotNull`, `Assert.NotEmpty` or a count where the IDs, statuses and reasons should be checked |
| 4 | **Is the set-up correct?** | a handler that answers every request the same way when the test needs per-parcel answers; `It.IsAny` where the argument matters |
| 5 | **Are the edge cases there?** | only the happy path; no "another courier" or "no outcome" case |
| 6 | **Is it deterministic?** | `DateTime.Now`, random data in `InlineData`, order dependence, shared static state |
| 7 | **Does it follow the conventions?** | names, Arrange/Act/Assert, Theory versus Fact, business language in Gherkin, context injection |
| 8 | **Does it duplicate a better test?** | an existing test already proves the same thing at the right level |

## 5. Proof that two kept tests are meaningful

### Proof 1: `UploadAsync_ScanBelongsToAnotherCourier_DoesNotSendItAndReturnsNoOutcome` (kept)

**The break:** `ApiShipmentUploader` was changed so it no longer skips another courier's scans (`… && false` added to the courier check).

```text
UploadAsync_ScanBelongsToAnotherCourier_DoesNotSendItAndReturnsNoOutcome [FAIL]
   Assert.Empty() Failure: Collection was not empty
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

The scan of courier CR-002 was sent under CR-001's sign-in, which the API would have refused with a 403. The code was restored, and the test passes again.

### Proof 2: `UploadAsync_BadRequestNamesSecondScan_RejectsOnlyTheNamedScan` (kept unchanged)

**The break:** a 400's named scan was mapped to the first scan of the batch instead of the one at the named index (`batch[pair.Key]` became `batch[0]`).

```text
UploadAsync_BadRequestNamesSecondScan_RejectsOnlyTheNamedScan [FAIL]
   Assert.Equal() Failure: Values differ
Expected: scan::01a1e054-7026-7212-9fa4-ce815d373184
Actual:   scan::01a1e054-7023-72b2-a811-2409d801896f
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

The wrong scan would have been marked Failed, and the scan the server actually refused would have stayed Pending, to be refused again. The code was restored, and the test passes again.

### Proof 3 (draft 2, beyond the two required): "A courier cannot skip or reverse a shipment status" (kept)

**The break:** the status chain was changed to let a Created shipment go straight to InTransit (`TrackingStatusRules`).

```text
A courier cannot skip or reverse a shipment status(trackingNumber: "APX-53000006", currentStatus: "Created", attemptedStatus: "InTransit", ...) [FAIL]
 Expected: Conflict
 Actual:   OK
Failed!  - Failed: 1, Passed: 2, Skipped: 0, Total: 3
```

Exactly the row that skips from Created failed; the backward and OutForDelivery rows still passed. The code was restored, and all three pass again.

The broken code was never committed.

## 6. What was learnt

**What Copilot was good at:**
- The *shape* of a test suite. It found the right fake handler, the right helpers and sensible test names.
- Good table rows: the five allowed moves; a backward, a skipping and an OutForDelivery refusal.
- Several exact, record-level assertions that were right first time: the 404, the named 400 and the rejection tests.

**What it got wrong:**
- **Things it could not see, it guessed:**
  - that an error message is the server's raw text;
  - that a refused move is a 400.

  Both guesses were plausible, and both were wrong, because the real answer is a deliberate design decision recorded elsewhere (`ApiErrors.FriendlyMessage`, ADR 0026). **Every expected value in a generated test has to be checked against the code or the contract, not trusted.**
- **Syntax and data slips a compiler or a parser catches:** multi-line raw strings that do not compile, and JSON with a stray `}`.
- **Context it was not shown:** it redefined two steps that already existed in another step class, which broke a working feature. A generated file must be run together with *everything* else, not just alone.
- **A test that cannot fail:** the 403/409 assertion ran on an empty list. Only reading the assertion, not the green tick, catches this.

**How the review was done.** Each draft was run three ways:
1. exactly as produced;
2. with only its mechanical faults fixed, so each remaining failure could be traced to a wrong belief about the system;
3. after the review.

The tests kept were then proven by breaking production code. That made the difference between a draft that "looks right" and one that is right.
