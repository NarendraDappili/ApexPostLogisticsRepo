# ApexPost testing strategy (Week 5)

This document explains how ApexPost is tested: **what** we test, **at which level**, **with which tools**, and **why**. It was written and committed **before** any Week 5 test, as the brief requires. The existing-test review (section 6) records a decision for every test class that existed when Week 5 began. The reflection (section 12) is written at the end of the week.

| | |
|---|---|
| Starting point | 1151 tests, all passing, zero-warning Release build (13 November 2026, commit `dd8a4c2`) |
| Branch | `week5-testing` (steps merged into it with merge commits; then into `master`) |
| Related | [TDD-LOG.md](TDD-LOG.md) and [COPILOT-LOG.md](COPILOT-LOG.md) (added later in Week 5), [docs/testing.md](docs/testing.md) (how to run, CI), [docs/week5/PRESENTATION.md](docs/week5/PRESENTATION.md) (the five-minute viva) |

---

## 1. The idea in one sentence

**Test each risk at the cheapest level that can actually prove it, and use the real dependency wherever the risk lives in that dependency.**

Everything below follows from that sentence:
- Rules that can be decided from their inputs are unit tests.
- Anything that depends on what SQL Server, Couchbase Lite or the ASP.NET Core pipeline does is tested against the real thing.
- The few promises a business person would recognise are also written as Gherkin scenarios.

The number of tests is not a goal. Coverage is used as evidence of blind spots, never as a target.

---

## 2. Risk map

The parts of ApexPost most likely to break, or most costly if they break. **L** = likelihood, **I** = impact (H/M/L).

| # | Risk | L | I | Why it is likely | What it costs if it breaks | Main proof (level) |
|---|---|---|---|---|---|---|
| R1 | **Shipment status transitions and scan evaluation.** Created → PickedUp → InTransit → OutForDelivery → Delivered/Failed; late offline scans become history only. | M | H | A rule table plus `EvaluateScan`'s three branches; it changes whenever the chain changes | A parcel shows "Delivered" wrongly, or a valid doorstep scan is refused | Unit: the full transition matrix |
| R2 | **Duplicate tracking numbers.** A pre-check plus a unique index; a race becomes SQL error 2601/2627, then `DuplicateKeyException`, then 409. | M | H | Two layers must agree, and the race path appears only under concurrency | Two parcels share a number; tracking is corrupted | Unit (the pre-check, with Moq) + **SQL Server integration** (index and race) + BDD |
| R3 | **Scan de-duplication and idempotent upload.** The scan ID is the key; a retry never stores twice. | H | H | Retries *resend by design* (Polly), and offline devices resend | Duplicate history; wrong counts | Unit (service) + API integration + BDD |
| R4 | **Offline outbox: no scan is ever lost.** Pending on failure, batches of 20, one sync at a time. | M | H | Background timing, network flaps, partial batch outcomes | A courier's proof of delivery disappears | Unit (Moq) + Couchbase Lite integration + BDD |
| R5 | **API authentication and authorization.** 401 versus 403, role rules, a courier uploading only as themselves, partner keys, HTTPS. | M | H | Policies, attributes and claims are spread across the pipeline | A security breach, or couriers locked out | **API integration with real JWT validation** |
| R6 | **Courier client resilience and token handling.** Retry only transient failures and safe requests; circuit breaker; timeouts; a 401 ends the sign-in. | M | H | Declarative Polly configuration is easy to mis-order | A retry storm, double uploads, or a courier stuck offline | Unit with a fake `HttpMessageHandler` + component tests |
| R7 | **Validation rules and their boundaries.** Tracking number `APX-` + 8 digits; weight 0.1–70 kg (tracking) and up to 30 kg (counter); phone; scan time at most 5 minutes ahead. | H | M | Boundaries; both the DTO attributes and the domain check | Bad data stored, or good data refused | Unit theories at each boundary |
| R8 | **API contract drift (v1/v2).** | M | H | Any DTO rename changes the wire format | The generated SDK, the courier app and the future MAUI app break silently | Contract test against the committed JSON |
| R9 | **Error handling and information leakage.** | L | H | One missed exception path | Stack traces or SQL shown to callers | API and Web integration |
| R10 | **Week 3 counter workflows.** Pricing, money orders, stock, transactions. | L | M | Large but stable code | Wrong charges or stock | Unit + SQL integration |
| R11 | **Persistence correctness.** Mappings, stored procedures, audit trail. | M | M | Migrations and procedures change independently of C# | Lost or wrong records; audit gaps | SQL Server integration |
| R12 | **Web app security pipeline (Weeks 2–3).** Cookies, antiforgery, roles, rate limits, headers. | L | H | Middleware order matters | Account or data exposure | Web integration |

The brief's examples map to R1 (status transitions), R2 (duplicate tracking numbers), R3 (scan de-duplication), R5 (JWT role rules) and R6 (client retry behaviour). R4 is ApexPost's own costliest risk.

---

## 3. Test pyramid for ApexPost

### 3.1 The levels

| Level | Meaning in ApexPost | Typical speed | Tools |
|---|---|---|---|
| **Unit** | One class or a small cluster. No database, no files written, no sockets, no system clock. Collaborators are fakes or mocks. | milliseconds | xUnit, Moq, `FakeTimeProvider`, fake `HttpMessageHandler` |
| **Integration** | Our code plus **one real dependency it owns**: SQL Server (EF Core, procedures, constraints), Couchbase Lite (SQL++, indexes, listeners), the ASP.NET Core pipeline (`WebApplicationFactory`), or the courier app's real DI composition | 0.1 s to a few seconds | xUnit fixtures, throwaway databases, `WebApplicationFactory` |
| **Acceptance (BDD)** | A business scenario in Gherkin, run through the public surface | seconds | Reqnroll |
| *Guards* | Contract drift, architecture rules (no `new HttpClient()`, domain without Couchbase types), the schema script matching migrations | fast | xUnit; counted at the level of what they touch |

### 3.2 What is tested where

| Behaviour | Unit | Integration | BDD |
|---|---|---|---|
| Status transitions and scan evaluation (R1) | ✔ full matrix | ✔ through the API | ✔ main path |
| Duplicate tracking number (R2) | ✔ pre-check (Moq) | ✔ unique index and race (SQL Server) | ✔ refusal |
| Idempotent scan upload (R3) | ✔ service | ✔ endpoint, parallel uploads | ✔ duplicate skipped |
| Outbox (R4) | ✔ batching, failure, single flight (Moq) | ✔ real Couchbase Lite store | ✔ offline then synced |
| Auth rules (R5) | — (a unit test cannot prove pipeline configuration) | ✔ real JWT, roles, courierId, partner keys | ✔ via a role-bound token |
| Client resilience and errors (R6) | ✔ `ApexApiClient` on a fake handler | ✔ the Polly pipeline in the app's composition | — |
| Validation boundaries (R7) | ✔ theories | ✔ ProblemDetails shape | ✔ invalid weight outline |
| Contract (R8), errors (R9) | — | ✔ | — |
| Week 3 workflows (R10), persistence (R11), Web pipeline (R12) | ✔ services | ✔ SQL Server, Web host | — |

### 3.3 Proportions

| Level | At the start of Week 5 | Target at the end (approx.) |
|---|---|---|
| Unit | ~477 cases (41%) | ~45–50% |
| Integration | ~674 cases (59%) | ~45–50% |
| BDD | 0 | ~2–3% (about 15–20 scenario cases) |

**Why ApexPost's pyramid has a wide middle.** The costliest risks live at *boundaries*:
- R2: a unique index;
- R4: SQL++ queries and listeners;
- R5: the authentication pipeline;
- R8: the wire contract.

A unit test cannot prove any of these, and replacing such tests with mocks would swap proof for assumption (section 7.3). So the unit share grows only through tests that belong at unit level: Moq interaction tests, the HTTP client suite, the TDD rule, and pure tests moved down out of integration projects. The integration share is not cut for the ratio's sake.

### 3.4 How a level is chosen

1. If the outcome can be decided **from the inputs alone**, it is a unit test.
2. If it depends on **what the database, engine or pipeline does** (translation, constraints, ordering, concurrency, authentication), it is an integration test with the real thing.
3. If a **business person** would recognise it as a promise to a customer, courier or partner, it is *also* a scenario, but only for the main path and the key refusals.
4. **Each risk has one primary home.** Boundaries and permutations live at the lowest level that can prove them.

---

## 4. Test projects

### 4.1 Layout

```
tests/
  ApexPost.UnitTests/                        Unit: no database, no files written, no sockets, no system clock
  ApexPost.Infrastructure.IntegrationTests/  SQL Server: EF Core, procedures, audit trail
  ApexPost.Api.IntegrationTests/             API host (WebApplicationFactory) + SQL Server + real JWT
  ApexPost.Web.IntegrationTests/             Web host (WebApplicationFactory) + SQL Server
  ApexPost.Courier.Offline.IntegrationTests/ Couchbase Lite in a temporary folder
  ApexPost.Courier.IntegrationTests/         The courier app's composition + Couchbase Lite + fake API
  ApexPost.Specs/                            Reqnroll features and bindings
```

The solution file groups them into **Unit**, **Integration** and **Specs** folders, so Test Explorer shows the pyramid.

In place since step S3:
- **457 tests in the unit project and 694 in the integration projects.** The move changed no test: the list of test cases is identical before and after, compared by class, method and arguments.
- **The SQL Server projects share one helper** (`tests/Shared/TestSqlServer.cs`), which finds the server as section 9.4 describes.
- **CI runs the unit tests before SQL Server starts**, then each integration project.

### 4.2 Why this layout and not a single `ApexPost.IntegrationTests`

The brief allows "your documented equivalent structure". The integration tests are split **by the runtime dependency they boot** because of three facts in the code:
1. **Two global `Program` classes.** `ApexPost.Web` and `ApexPost.Api` both declare `public partial class Program`. One assembly using `WebApplicationFactory<Program>` for both does not compile (CS0433) without extern aliases.
2. **Native and stateful dependencies.** Couchbase Lite loads native LiteCore, and the SQL tests create real databases. Keeping each in its own assembly keeps xUnit's parallel scheduling, failures and set-up costs local.
3. **The courier app is an executable** whose types are `internal`. Its client and sync code moves to a class library, `ApexPost.Courier.Core` (section 10), which `ApexPost.UnitTests` and `ApexPost.Specs` can reference cleanly.

The unit tests, by contrast, have no runtime dependency, so they are **consolidated into one project**. `dotnet test tests/ApexPost.UnitTests` gives the fast feedback loop and needs nothing installed.

---

## 5. Framework choice: xUnit

| Criterion | xUnit | NUnit | For ApexPost |
|---|---|---|---|
| Consistency with existing tests | all 1151 tests | none | **xUnit.** Switching would rewrite every test with no gain in confidence. |
| Test isolation | **a new class instance per test**, so fields cannot leak between tests | one instance per fixture by default, so fields leak unless reset in `[SetUp]` | xUnit enforces our "no shared mutable state" rule structurally |
| Set-up and teardown | constructor, `IDisposable`, `IAsyncLifetime`; `IClassFixture`/`ICollectionFixture` for expensive shared resources (already used for SQL databases and the API host) | `[SetUp]`/`[TearDown]`, `[OneTimeSetUp]` | equal power; xUnit's is plain C# and already in use |
| Parameterised tests | `[Theory]` + `[InlineData]` / `[MemberData]` / `TheoryData<>` | `[TestCase]`, `[TestCaseSource]`, `[Values]`/`[Combinatorial]` | NUnit is richer for combinations, but our theories are explicit boundary tables, where `InlineData` reads best |
| Reqnroll support | `Reqnroll.xUnit` | `Reqnroll.NUnit` | equal |
| Team familiarity | the whole project history | none | xUnit |

**Decision:** xUnit only, across the whole solution, including the Reqnroll specs. We stay on **xUnit v2** (2.9.3), the version all existing projects use. Moving to xUnit v3 changes the runner packaging and the Reqnroll adapter, so it is noted as future work rather than mixed into this week.

Accepted limits:
- xUnit v2 has no `[Category]`. The level is the project (section 4); `[Trait]` is used only for cross-cutting groups.
- Visual Studio discovers tests in one process and runs them in another, so **theory data never contains per-process random values** (keys, tokens, GUIDs). Theories pass named cases and build such values inside the test.

---

## 6. Review of the existing tests

Every test class that existed when Week 5 began, with its decision and the reason.

**Decisions:**
- **Kept**: unchanged.
- **Kept (moved)**: unchanged except for its project and namespace.
- **Modified**: changed in the step named.
- **Replaced**: superseded by a better test, which is named.
- **Removed**: deleted, with the reason.

The questions asked of each class:
1. Does it protect a risk (section 2) or a contract or architecture rule?
2. Is it at the right level (section 3.4)?
3. Is it deterministic (section 8)?
4. Can it fail for the right reason, or does it mirror the implementation?
5. Is it duplicated by a better test?

Steps: **S2** is the courier library extraction; **S3** the project restructure; **S4** determinism.

### 6.1 `ApexPost.Domain.Tests` → `ApexPost.UnitTests` (all pure unit)

| Class | Cases | Risk | Decision | Reason |
|---|---|---|---|---|
| `BranchAndCounterTests` | 5 | R10 | Kept (moved) | Branch and counter invariants; pure domain |
| `CodeValueTests` | 8 | R7 | Kept (moved) | Code format rules with boundary theories |
| `CounterTransactionTests` | 3 | R10 | Kept (moved) | Transaction invariants; the default time it reads is removed from production in S4 (the tests pass explicit times) |
| `IdentityAndCodeTests` | 14 | R7 | Kept (moved) | ID and code value objects |
| `MoneyTests` | 8 | R10 | Kept (moved) | Money arithmetic and currency rules |
| `ParcelMeasurementTests` | 10 | R7 | Kept (moved) | Counter weight (≤ 30 kg) and dimension boundaries |
| `PersistenceReadyDomainTests` | 14 | R11 | Kept (moved) | Domain shapes EF Core relies on (constructors, keys) |
| `ProductAndInventoryTests` | 6 | R10 | Kept (moved) | Stock invariants |
| `ShipmentAndMoneyOrderTests` | 4 | R10 | Kept (moved) | Week 3 shipment and money order rules |
| `StaffMemberTests` | 9 | R12 | Kept (moved) | Staff roles and identity |
| `TrackedShipmentTests` | 39 | R1, R7 | Kept (moved) | Status rules, scan evaluation, weight 0.1–70 kg; the full transition matrix is added beside it |
| `TrackingValueObjectTests` | 33 | R7 | Kept (moved) | Tracking number, recipient, scan ID formats |

### 6.2 `ApexPost.Application.Tests` → `ApexPost.UnitTests` (unit, hand-written fakes)

| Class | Cases | Risk | Decision | Reason |
|---|---|---|---|---|
| `AuditLogServiceTests` | 19 | R11 | Kept (moved) | Audit queries and filters through fakes |
| `CounterTransactionWorkflowServiceTests` | 17 | R10 | Kept (moved) | Checkout, void and refund workflow |
| `InventoryServiceTests` | 9 | R10 | Kept (moved) | Stock rules |
| `ManagementServiceTests` | 3 | R10 | Kept (moved) | Manager operations |
| `MoneyOrderWorkflowServiceTests` | 12 | R10 | Kept (moved) | Money order rules and identity checks |
| `ParcelStatusServiceTests` | 4 | R10 | Kept (moved) | Week 3 parcel status lookups |
| `PriceRevisionServiceTests` | 7 | R10 | Kept (moved) | Price revisions |
| `RequestValidatorTests` | 2 | R7 | Kept (moved) | The shared validator |
| `RetailPriceCalculatorTests` | 3 | R10 | Kept (moved) | Retail pricing |
| `ScanUploadServiceTests` | 12 | R3, R1 | Kept (moved) | Idempotent upload over a stateful in-memory store. Stateful behaviour across calls is clearer with a fake than with mocks; the Moq interaction tests are added beside it. |
| `ShipmentAcceptanceRuleTests` | 4 | R7 | Kept (moved) | Counter acceptance rules |
| `ShipmentTrackingServiceTests` | 31 | R1, R2 | Kept (moved) | Tracking use cases with the in-memory store; the Moq tests for the duplicate and race paths are added beside it |
| `ShippingPriceCalculatorTests` | 8 | R10 | Kept (moved) | Shipping price bands |
| `ShippingWorkflowServiceTests` | 15 | R10 | Kept (moved) | Week 3 shipping workflow; Moq tests on `IShipmentRepository` are added beside it |
| `StockAdjustmentServiceTests` | 6 | R10 | Kept (moved) | Stock adjustments; the default time is removed from production in S4 |

### 6.3 `ApexPost.Infrastructure.Tests` → `ApexPost.Infrastructure.IntegrationTests`

| Class | Cases | Level | Risk | Decision | Reason |
|---|---|---|---|---|---|
| `ApexPostModelTests` | 33 | unit | R11 | Kept (moved to UnitTests) | Reads EF Core model metadata with no connection |
| `DesignTimeFactoryTests` | 1 | unit | R11 | Kept (moved to UnitTests) | Design-time factory configuration; no database |
| `Pbkdf2PasswordHasherTests` | 8 | unit | R12 | Kept (moved to UnitTests) | Password hashing; pure |
| `SimulatedExternalServiceTests` | 10 | unit | R10 | Kept (moved to UnitTests) | Simulated external service limits; no I/O |
| `StoredProcedureMigrationTests` | 3 | unit | R11 | Kept (moved to UnitTests) | Checks the migration text that creates the procedures |
| `AuditLogQueryTests` | 7 | SQL | R11 | Kept | Audit queries need real SQL translation |
| `AuditTrailSchemaTests` | 3 | SQL | R11 | Modified (S4) | Audit schema on SQL Server; the record times become fixed instants instead of the system clock |
| `CatalogueAndRepositoryGuardTests` | 2 | SQL | R11 | Kept | Repository guards against the real schema |
| `CheckoutPersistenceTests` | 6 | SQL | R10, R11 | Kept | Checkout persisted in one transaction |
| `DataChangeAuditTests` | 13 | SQL | R11 | Modified (S4) | Audit interceptor on real saves; one security-event time becomes a fixed instant |
| `EfAuditStoreTests` | 3 | SQL | R11 | Modified (S4) | Audit store; the fixed instant replaces the system clock |
| `EfRepositoryMappingTests` | 8 | SQL | R11 | Kept | Mappings round-trip through SQL Server |
| `GeneratedSqlTests` | 4 | SQL | R11 | Kept | Checks the SQL EF Core generates; only a real provider can |
| `ParallelReportTests` | 3 | SQL | R10 | Modified (S4) | Reports for "today": the seeded transactions and the queried day become one fixed date, so a run crossing midnight UTC cannot fail |
| `QueryServiceTests` | 6 | SQL | R10 | Modified (S4) | Same midnight fix as above |
| `SeedingAndRateCacheTests` | 3 | SQL | R10 | Kept | Seed data and the rate cache on a real database |
| `StoredProcedureTests` | 15 | SQL | R11 | Modified (S4) | Procedures only exist on SQL Server; same midnight fix |
| `TrackingPersistenceTests` | 17 | SQL | R2, R3 | Kept | **The unique index and the duplicate-key race (R2)** and scan storage. These are the tests a mock can never replace. |

### 6.4 `ApexPost.Api.Tests` → `ApexPost.Api.IntegrationTests`

| Class | Cases | Level | Risk | Decision | Reason |
|---|---|---|---|---|---|
| `ApiCorrelationContextTests` | 3 | unit | R9 | Kept (moved to UnitTests) | Correlation context; pure |
| `BearerTokenHandlerTests` | 6 | unit | R5 | Kept (moved to UnitTests) | The generated client's token handler on a fake handler; no host |
| `ControllerArchitectureTests` | 8 | unit | guard | Kept (moved to UnitTests) | Thin-controller and routing rules by reflection |
| `CustomRuleTests` | 20 | unit | R7 | Kept (moved to UnitTests) | Custom validation rules with a fixed clock, including the 5-minute skew boundary |
| `TrackingNumberRouteConstraintTests` | 14 | unit | R7 | Kept (moved to UnitTests) | Route constraint; pure |
| `AccessTokensTests` | 3 | API | R5 | Kept | Token acquisition for the generated client |
| `ApiCompositionTests` | 7 | API | R5, R9 | Kept | Start-up refuses missing or invalid settings |
| `AuthenticationTests` | 29 | API | R5 | Kept | 401 cases with real JWT validation. Token lifetimes are relative to the system clock because Microsoft.IdentityModel validates lifetime against it; margins of minutes against a 5-minute skew make the outcome fixed (the documented exception in section 8). |
| `AuthorizationTests` | 15 | API | R5 | Modified (S4) | 403 rules and courierId ownership; scan times become fixed instants |
| `ClientGenerationTests` | 5 | API | R8 | Kept | The NSwag client matches the committed contract |
| `ContractTests` | 5 | API | R8 | Kept | Drift guard against `contracts/*.json` |
| `DevelopmentTokenTests` | 3 | API | R5 | Kept | The development token endpoint; lifetime checked against the system clock under the same JWT exception |
| `ErrorHandlingTests` | 14 | API | R9 | Kept | 500 without internals outside Development |
| `GeneratedClientTests` | 6 | API | R8 | Kept | The generated client against the real API |
| `OpenApiDocumentTests` | 14 | API | R8 | Kept | The document transformer and versions |
| `OpenApiMetadataTests` | 42 | API | R8 | Kept | Operation IDs, summaries, declared responses |
| `PartnerApiKeyTests` | 16 | API | R5 | Kept | Partner key rules |
| `PartnerShipmentTests` | 4 | API | R5 | Kept | The partner view leaves out recipient data |
| `RequestValidationTests` | 44 | API | R7, R9 | Kept | Validation through the real pipeline proves the ProblemDetails shape; the rules themselves are unit-tested |
| `RoleAccessTests` | 5 | API | R5 | Kept | Each role's allowed work on a real database |
| `ScanUploadEndpointTests` | 10 | API | R3 | Modified (S4) | Idempotent upload end to end. Scan times become fixed instants: far past for valid scans, far future for refused ones. The parallel-upload test asserts only what holds on every interleaving. |
| `ShipmentEndpointTests` | 26 | API | R1, R2 | Kept | Verbs, status codes, Location, 409 for duplicates |
| `SwaggerUiTests` | 8 | API | R8 | Kept | Swagger UI in Development only |
| `TransportSecurityTests` | 7 | API | R5 | Kept | HTTPS redirection and HSTS |
| `TryItOutTests` | 2 | API | R8 | Kept | Each documented example works against a real database |
| `VersioningTests` | 13 | API | R8 | Kept | v1 deprecation headers, v2 shape |

### 6.5 `ApexPost.Web.IntegrationTests` (name unchanged)

| Class | Cases | Level | Risk | Decision | Reason |
|---|---|---|---|---|---|
| `AuditDisplayAndCsvTests` | 13 | unit | R11 | Kept | Unit-level, but it tests MVC view helpers. `ApexPost.UnitTests` does not reference the Web app (the `Program` clash), so it stays beside the Web app's tests. |
| `DemoSeedHostedServiceTests` | 7 | unit | R10 | Kept | Same reason |
| `AuditLogScreenTests` | 8 | Web | R11 | Kept | Audit screen end to end |
| `AuditTrailTests` | 7 | Web | R11 | Kept | Audit trail written by real requests |
| `AuthenticationTests` | 45 | Web | R12 | Modified (S4) | Cookie sign-in, roles and lockout; the sales-report test no longer depends on the date at midnight UTC |
| `PartnerApiTests` | 3 | Web | R12 | Kept | Partner endpoint limits |
| `ProblemDetailsMappingTests` | 10 | Web | R9 | Kept | Error mapping |
| `ScreenEdgeCaseTests` | 6 | Web | R7 | Kept | Edge inputs on screens |
| `SecurityPipelineTests` | 13 | Web | R12 | Kept | Headers, antiforgery, rate limits |

### 6.6 `ApexPost.Courier.Offline.Tests` → `ApexPost.Courier.Offline.IntegrationTests`

| Class | Cases | Level | Risk | Decision | Reason |
|---|---|---|---|---|---|
| `DomainIndependenceTests` | 3 | guard | guard | Kept (moved to UnitTests) | Domain has no Couchbase types (reflection and source scan) |
| `FakeShipmentUploaderTests` | 5 | unit | R4 | Kept (moved to UnitTests) | The harness's fake uploader is production code the harness still uses |
| `ScanIdTests` | 14 | unit | R3 | Kept (moved to UnitTests) | Version 7 GUID scan IDs sort by time |
| `ShipmentScanTests` | 17 | unit | R4, R7 | Kept (moved to UnitTests) | Scan model rules |
| `CouchbaseScanRepositoryTests` | 13 | Couchbase | R4 | Kept | Key-value CRUD on a real store |
| `CourierConsoleTests` | 10 | Couchbase | R4 | Kept | The Part A harness end to end |
| `CourierDatabaseTests` | 12 | Couchbase | R4 | Kept | Lifecycle, single-process rule, safe close |
| `OutboxServiceTests` | 13 | Couchbase | R4 | Kept | The outbox with a real store; the Moq unit tests are added beside it and prove interactions, not the store |
| `ScanChangeMonitorTests` | 9 | Couchbase | R4 | Modified (S4) | Change listener; one scan time becomes a fixed instant |
| `ScanDocumentMapperTests` | 13 | Couchbase | R4 | Kept | Builds real Couchbase documents (native engine) |
| `ScanQueryTests` | 20 | Couchbase | R4 | Kept | Parameterised SQL++ and index use via `Explain()`: a mock cannot prove this |

### 6.7 `ApexPost.Courier.Tests` → `ApexPost.Courier.IntegrationTests`

| Class | Cases | Level | Risk | Decision | Reason |
|---|---|---|---|---|---|
| `ContractCompatibilityTests` | 4 | guard | R8 | Modified (S3, S4): moved to UnitTests | The client's records match `contracts/v2.json`. The records moved to `ApexPost.Courier.Core` in S2, so the test follows them to the unit project. The scan time becomes a fixed instant. |
| `SyncHistoryTests` | 3 | unit | R4 | Kept (moved to UnitTests, S3) | Sync history moved with the sync code to `ApexPost.Courier.Core` in S2 |
| `ScreenOutputTests` | 3 | unit | — | Kept | Console-only output; stays with the console app |
| `ApexApiClientTests` | 13 | component | R6 | Modified (S2) | Typed client from the factory, with the token and address. The "never `new HttpClient()`" source guard now scans `ApexPost.Courier.Core` as well, because the client moved there. Without that change, the guard would have kept passing while no longer looking at the client. The focused unit-level client suite is **added** beside it and does not replace it: this class proves the DI wiring. |
| `ApiShipmentUploaderTests` | 10 | component | R3, R4 | Modified (S4) | Scan times become fixed instants (far future for the refused case) |
| `AutoSyncTests` | 5 | component | R4 | Modified (S4) | Fixed scan times; the wait helper uses a `Stopwatch` instead of the system clock |
| `CourierAppTests` | 19 | component | R4, R7 | Modified (S4) | Whole scripted sessions; the "future time" input becomes a fixed far-future time |
| `ErrorAndJsonHandlingTests` | 22 | component | R6, R9 | Modified (S4) | ProblemDetails and JSON handling; fixed scan time |
| `OutForDeliveryTests` | 11 | component | R1 | Kept | The Out for Delivery action |
| `ResilienceTests` | 23 | component | R6 | Modified (S4) | The Polly pipeline in the real composition; fixed scan time |
| `TokenHandlingTests` | 18 | component | R6, R5 | Modified (S4) | Token expiry uses fixed instants (the handler takes `TimeProvider`); the wait helper uses a `Stopwatch` |

**Summary:** 102 classes. No class is removed or replaced. The existing tests protect real risks, and Week 5's additions sit beside them because they prove different things:
- **Kept unchanged:** 42 classes.
- **Kept (moved):** 42 classes.
- **Modified:** 18 classes, for determinism or because their code moved.

Helper classes (fakes, factories, test databases) move with the tests that use them and are not listed.

---

## 7. Isolation: mocks, fakes and real dependencies

### 7.1 Which test double, when

| Double | When | Examples |
|---|---|---|
| **Moq mock** | The test is about an *interaction*: was it called, how often, with what? | `Add` never called for a duplicate tracking number; `UploadAsync` called exactly 3 times for 45 scans; `SaveChangesAsync` once |
| **Moq stub** | A collaborator just has to answer | `FindAsync` returns an existing shipment |
| **Hand-written fake** | The test needs *state or behaviour across many calls* | `InMemoryTrackingStore`, `FakeApi` (the API's rules in memory), `StubServer` (a fake `HttpMessageHandler`), `FakeNetwork` |
| **The real thing** | The behaviour *is* the dependency | SQL Server, Couchbase Lite, the ASP.NET Core pipeline |

Existing fakes are **not** rewritten in Moq. A stateful fake expressed as a chain of `Setup` calls reads worse and proves the same thing. Moq is used where it is the clearest way to say what the test means. Moq is pinned to an exact 4.20.x patch (never 4.20.0, which shipped SponsorLink).

### 7.2 Moq: units and what the tests show

| Unit under test | Mocked | Shown |
|---|---|---|
| `ShipmentTrackingService` | `ITrackedShipmentRepository`, `ITrackingQueries`, `IUnitOfWork` | A duplicate number: `Add` and `SaveChangesAsync` are `Times.Never` called. A race: `SaveChangesAsync` throws `DuplicateKeyException`, which becomes `TrackingNumberInUseException`. `It.Is<TrackedShipment>(…)` checks what is added. |
| `ShippingWorkflowService` (Week 3) | `IShipmentRepository` and its other ports | Invalid input never touches the repository; a valid shipment is added once with the expected weight |
| `OutboxService` | `IScanRepository`, `IShipmentUploader` | Nothing Pending: no upload. 45 scans: 3 batches of at most 20. A failed upload marks nothing Synced. Two simultaneous syncs: one upload. |
| `ScanRecorder` (the TDD rule) | `IScanRepository` + `FakeTimeProvider` | A double scan inside the window is not saved; one outside it is |

### 7.3 What a mock does not prove

| Mock | Proves | Does **not** prove | Proven instead by |
|---|---|---|---|
| `ITrackedShipmentRepository.FindAsync` returns `null` | the service's decision flow | that the unique index exists, or that a race becomes 2601/2627 and then a 409 | `TrackingPersistenceTests`, `ShipmentEndpointTests` (SQL Server) |
| `IScanRepository` returns 45 Pending scans | batching and marking | that the SQL++ query returns Pending scans oldest first through the index | `ScanQueryTests` (Couchbase Lite, `Explain()`) |
| `IShipmentUploader` answers Accepted | the outbox marks scans Synced | that the server really accepts that scan, or the JSON shape | `ScanSync.feature`, contract tests |
| `IUnitOfWork.SaveChangesAsync` called once | one commit per command | that the commit is atomic, or that the audit interceptor runs | SQL Server integration |

### 7.4 HTTP: the Week 4 Part D client

`ApexApiClient` is tested on a **fake `HttpMessageHandler`** (`StubServer`), not `Moq.Protected()`. The fake records each request and its body, answers in turn, and can delay, with no string-named protected-method set-ups that break silently on refactoring.

A focused **unit-level** suite builds the client directly on `new HttpClient(stub)`, with no host, no store and no DI. It covers:
- a 200 with JSON;
- 404;
- 500 (only a reference, never server internals);
- timeout and caller cancellation;
- a ProblemDetails response.

The retry pipeline stays at component level, because there the registration *is* the behaviour.

**No real network:** unit tests never open a socket. `WebApplicationFactory` uses the in-memory `TestServer`. An architecture test fails if a test project starts Kestrel on a port or opens a `TcpListener`.

---

## 8. Determinism

| Threat | Rule |
|---|---|
| **The system clock** | Production code takes `TimeProvider`; the four places that still read `DateTimeOffset.UtcNow` are changed. Tests use one of two kinds of fixed instant. **`FakeTimeProvider`** is used where the code under test takes a `TimeProvider` and the test is about time arithmetic (boundaries, expiry, windows). **Far-past or far-future constants** are used where the test only needs "clearly valid" or "clearly in the future" through a host. |
| **The JWT exception** | Microsoft.IdentityModel validates token lifetime against the system clock, so API tests that *send* tokens create them relative to the real clock with margins of minutes against a 5-minute skew. The outcome cannot flip. This is the only accepted clock read, and it is listed in section 6. |
| **Waiting** | Polling helpers measure their own deadline with a `Stopwatch`. A deadline only bounds a wait; it never decides the outcome. |
| **Random values** | Allowed for identity (unique database names, scan IDs); never in theory data or in an expected value |
| **Test order** | No test depends on another. Tests that share a database use unique tracking numbers. |
| **Shared mutable state** | No mutable static fields. Reqnroll state goes through context injection. |
| **Concurrency** | A race test asserts only what holds on *every* interleaving (no double storage, a consistent 409 body). It never asserts that a race happened. |

---

## 9. Data access strategy

### 9.1 What each approach proves

| Approach | Used for | Proves | Does **not** prove |
|---|---|---|---|
| **Mocked repository interface** | Application service unit tests | the service's decisions and calls | queries, constraints, transactions, mapping, concurrency |
| **SQLite in-memory** | **Not used** (9.2) | — | — |
| **SQL Server test database**: a throwaway database per fixture, built from the real migrations and dropped afterwards | repositories, query services, stored procedures, the audit interceptor, API and Web integration tests | T-SQL translation, unique and filtered indexes, rowversion concurrency, procedures, real transactions, the duplicate-key mapping | production-scale performance (see `PERFORMANCE.md`), or a different SQL Server version or collation |
| **Couchbase Lite in a temporary folder** | the scan repository, SQL++ queries, indexes, the change listener, the outbox with a real store | key-value CRUD, parameterised SQL++, index use, listener lifecycle | behaviour on Android or iOS storage |

### 9.2 Why not SQLite in-memory

SQLite would test a different database from the one ApexPost ships with:
- **11 stored procedures** exist in Infrastructure. SQLite has none.
- **Six rowversion concurrency tokens.** SQLite has no `rowversion`, so concurrency conflicts would never occur.
- **The duplicate-key mapping** (`EfUnitOfWork`) recognises SQL Server error numbers 2601 and 2627. SQLite raises a different exception, so R2 would be proven only for a database we do not use.
- **`DateTimeOffset` ordering.** EF Core's SQLite provider cannot translate ordering or comparison on `DateTimeOffset`. Scan history and the audit trail order by it.
- **Filtered indexes, included columns, `ExecuteUpdate` and raw SQL** translate differently or not at all.

### 9.3 Why mocking `DbContext` or `DbSet` gives false confidence

A mocked `DbSet` runs the query as **LINQ to Objects in memory**, not as SQL:
- A query that **SQL Server cannot translate** passes the test and throws in production.
- **String comparison, case sensitivity and null semantics** follow C#, not the database collation.
- **Unique indexes, foreign keys, cascades and concurrency tokens do not exist**, which hides exactly R2, R3 and R11.
- **Change tracking, `Include`, owned types and interceptors** (the audit columns) never run.
- The set-up **states the answer the test then checks**, so the test proves only that the mock returns what it was told.

`DbContext` is therefore never mocked. Repositories are tested on SQL Server, and the services above them use mocked *repository interfaces*, which is an honest seam.

### 9.4 The test database needs no manual set-up

The SQL Server used by the tests is found automatically:
1. `APEXPOST_TEST_SQLSERVER`, when set (CI sets it to its SQL Server container);
2. otherwise the local default instance;
3. otherwise LocalDB, `(localdb)\MSSQLLocalDB`, which Visual Studio installs;
4. otherwise the run **fails with a message** naming the variable. It never skips, because a skipped test reads as a passed one.

### 9.5 The API integration test

`WebApplicationFactory<Program>` hosts `ApexPost.Api` in memory over a throwaway SQL Server database, with **real JWT validation and a per-run signing key** (`TestTokens`). A fake authentication handler was rejected: it would bypass the issuer, audience, lifetime and role-claim configuration, which is exactly what R5 must prove. The test owns only the key.

---

## 10. The future MAUI courier app

The courier client and sync code (`ApexApiClient`, `AuthHeaderHandler`, the resilience pipeline, `ApiShipmentUploader`, `AutoSyncService`, `CourierSession`, `SyncHistory`, and the `ITokenStore`/`INetworkStatus` seams) moved from the console executable into a class library, **`ApexPost.Courier.Core`**, in step S2 ([ADR 0032](docs/adr/0032-courier-core-library-for-every-app-head.md)). The console app keeps only its screens and console adapters, and registers the shared logic with one call to `AddCourierCore()`.

- The Week 5 client, sync and BDD tests target code the MAUI app will reuse, so they carry over unchanged.
- The MAUI app will add a `SecureStorage` token store and a `Connectivity` network status behind the same seams. An abstract contract test class for each seam runs against today's implementation now, and against the MAUI one later.
- MAUI pages and navigation stay out of scope (the brief). View models will live in a plain class library, so they can be unit-tested without a device.

---

## 11. What we will not test, and why

| Not tested | Why |
|---|---|
| MAUI UI, pages, navigation; browser or front-end automation | Out of scope by the brief. Console screen text is checked only where it carries behaviour. |
| Framework internals (EF Core, ASP.NET Core routing, Polly's algorithms, xUnit, Reqnroll) | Not our code. We test **our configuration** of them. |
| Couchbase Lite and LiteCore internals | Vendor code. We test our queries, indexes and lifecycle. |
| The generated NSwag client's code | Generated and reproducible; proven by the contract drift test and the consumer run |
| Real networks (airplane mode, Wi-Fi), a real identity provider, real `SecureStorage` | Cannot be reproduced deterministically in CI; covered by the Week 4 demo script and manual evidence |
| Performance, load and penetration | Different tools and goals; `PERFORMANCE.md` covers query cost |
| Private methods, auto-properties, trivial DTOs | Covered through public behaviour; testing them directly would pin implementation details |
| Production databases, real email, external HTTP | Forbidden by the brief; simulated services stand in |

---

## 12. Reflection

*Written at the end of Week 5: what the strategy caught, what we would change, and which test type gave the most confidence per minute spent.*
