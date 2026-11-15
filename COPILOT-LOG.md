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
| Tool | GitHub Copilot in Visual Studio 2026, *(Chat mode and model: filled in when used)* |
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

*(Copilot's draft, unchanged, is copied here before any edit.)*

### Review

*(One row per generated test: kept, changed or rejected, and why.)*

## 3. Draft 2: a Gherkin feature with its step definitions

**Why this feature.** Changing a shipment's status at head office is a business promise in its own right:
- one step at a time;
- a refused move names the status allowed next;
- Delivered and Failed are final;
- only couriers may move a shipment.

The two hand-written features do not cover it. It reuses the existing support code (`HeadOfficeDriver`, `ScenarioState`, the hooks), so the review can check whether Copilot uses context injection and the driver, or invents static state and raw HTTP in the steps.

### Prompt 2 (as sent)

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

*(Copilot's draft, unchanged, is copied here before any edit.)*

### Review

*(One row per scenario and step class decision: kept, changed or rejected, and why.)*

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

*(For each: the production line broken on purpose, the failing run, then the restored code passing. The broken code was never committed.)*

## 6. What was learnt

*(Written after the review.)*
