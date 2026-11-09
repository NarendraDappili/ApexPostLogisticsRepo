# Testing

## Commands

```powershell
dotnet build ApexPost.slnx -c Release -warnaserror
dotnet test ApexPost.slnx -c Release --no-build
```

The latest run had **925 tests passing, 0 failing, 0 skipped**, with a zero-warning build:
- 428 for Week 3;
- 128 for the Week 4 courier offline store (Part A);
- 274 for the Week 4 tracking API (Part B): 142 in the new API project, plus 72 Domain, 43 Application and 17 Infrastructure tests for the tracking model;
- 95 for Part C, all in the API project: the OpenAPI documents, Swagger UI, versioning, the committed contracts and the generated client.

The Week 3 error-handling tests also pass with `DOTNET_TieredCompilation=0` (55 tests).

### Line coverage

Coverage is measured with the collector built into `Microsoft.NET.Test.Sdk`, so no package is needed:

```powershell
dotnet test ApexPost.slnx -c Release --no-build --collect "Code Coverage;Format=cobertura"
```

Line coverage on 4 November 2026, excluding migrations, Razor views and `Program.cs`:

| Project | Before the final-review round | After |
|---|---|---|
| Domain | 90% | 94% |
| Application | 98% | 99% |
| Infrastructure | 96% | 99% |
| Web | 85% | 93% |
| **Total** | **92%** | **97%** |

Most of the remaining lines are the `catch (OperationCanceledException) … StatusCode(499)` branches for a client disconnect, and the demo seeder's seeding path.

The Week 4 courier offline projects, measured on 5 November 2026 with the same command: `ApexPost.Courier.Offline` **93%**, and `ApexPost.Courier.Offline.Harness` **84%**. The harness figure includes `Program.cs`, which only the manual runs exercise.

## CI on GitHub Actions

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) runs on every push and pull request to `master`, and on pull requests into the Week 4 part branches (`week4-part*` and `week4/part*`), so each step is checked on Linux before its part reaches `master`:
1. builds with `-warnaserror`;
2. fails if the EF Core model has changed without a migration (`dotnet ef migrations has-pending-model-changes`);
3. starts a throwaway SQL Server 2022 container with a random password;
4. runs every test, including the SQL Server tests;
5. reruns the error-handling tests with `DOTNET_TieredCompilation=0`, so they also run on fully optimized, inlined code (ADR 0018).

Every run of the workflow on GitHub has passed: 18 runs as of 2 November 2026. The [list of runs](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/workflows/ci.yml) is always current; the table below is a snapshot.

| Run | Trigger | Commit |
|---|---|---|
| [41039664846](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41039664846) | pull request #22 (Week 2 fixes) | `7548441` |
| [41044788151](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41044788151) | pull request #23 | `7766df8` |
| [41053844244](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41053844244) | pull request #24 | `b57d629` |
| [41237913225](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41237913225) | pull request #25 | `abbace7` |
| [41312501953](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41312501953) | pull request #26 | `bfdf82a` |
| [41313991662](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41313991662) | push to `master` | `b3e6f2e` |
| [41332216362](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41332216362) | push to `master` (Week 3 review fixes) | `a15e376` |
| [41396575257](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41396575257) | push to `master` (performance and security review) | `df17e7d` |
| [41401825333](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41401825333) | push to `master` (error mapping independent of JIT inlining) | `8f11f06` |
| [41409539228](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41409539228) | push to `master` (UI: sidebar layout) | `b3c1e09` |
| [41419904714](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41419904714) | push to `master` (UI) | `792df4c` |
| [41422278024](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41422278024) | push to `master` (UI: theme colours) | `2c2325c` |
| [41425827372](https://github.com/NarendraDappili/ApexPostLogisticsRepo/actions/runs/41425827372) | push to `master` (UI: theme and watermark) | `6c8659a` |

The other five runs are the pushes to `master` that merged #22–#26.

## Test projects

| Project | Tests | Needs a database |
|---|---|---|
| `ApexPost.Domain.Tests` | 153 (81 Week 3, 72 tracking) | No |
| `ApexPost.Application.Tests` | 152 (109 Week 3, 43 tracking) | No |
| `ApexPost.Infrastructure.Tests` | 145 (128 Week 3, 17 tracking) | SQL Server (except the model, migration, simulation, design-time factory and password-hasher tests) |
| `ApexPost.Web.IntegrationTests` | 110 | SQL Server (except the CSV, display and demo-seed guard tests) |
| `ApexPost.Courier.Offline.Tests` | 128 | No: each test opens its own Couchbase Lite database in a temporary folder |
| `ApexPost.Api.Tests` | 237 (142 Part B, 95 Part C) | SQL Server for the endpoint, versioning, "Try it out" and generated-client tests (one disposable database per run). The composition, routing, error-handling, custom-rule, OpenAPI, Swagger UI and contract tests use a connection string that is never opened. |

The final-review round added 136 tests:
- 117 in new test files;
- 15 audit-schema tests appended to `ApexPostModelTests`;
- 4 branch-check tests appended to `AuthenticationTests`.

No existing Week 3 test was changed or removed: since `1b2a090`, `git diff --numstat -- tests` shows additions only. The one exception is in Week 4 Part B: `DesignTimeFactoryTests` checks the name of the newest migration, which is now `AddShipmentTracking` rather than `AddAuditTrail`.

Week 4 Part C changed the Part B API tests in two ways, without weakening any assertion:
- they call the API at its versioned URLs (`/api/v1/…` instead of `/api/shipments…`), which shows the Part B contract unchanged at its new address;
- `ControllerArchitectureTests` and the OpenAPI tests now cover both versions.

A separate test (`VersioningTests`) keeps the old unversioned URLs working.

Each test project references only the production project it tests. The exception is `ApexPost.Api.Tests`, which also references `ApexPost.Client` and its console consumer, to run them against the real API. They use xUnit; the Web tests also use `Microsoft.AspNetCore.Mvc.Testing`.

## The test database

The SQL Server tests never touch the development database:

- **Disposable databases.** Each test run creates its own database, `ApexPostDb_Test_<guid>`. It is built with `Database.MigrateAsync()` from the **real migrations**, never `EnsureCreated`, and dropped afterwards. The test database therefore has exactly the production schema, procedures and indexes.
- **Shared within a run, isolated per test.** The Infrastructure tests share one database per run through an xUnit collection fixture. Each test seeds its own rows, with codes carrying a unique suffix, so tests never see each other's data.
- **One database per Web factory.** Each `WebApplicationFactory` gets its own database.
- **A guard.** It refuses any database whose name lacks the `ApexPostDb_Test_` prefix, so a misconfigured connection string cannot point the tests at real data.
- **Choosing the server.** It comes from `APEXPOST_TEST_SQLSERVER`, a connection string without a database name. The default is the local default instance with Windows authentication. CI starts a throwaway SQL Server container with a random password and sets this variable.

No test uses a real credential, external service or arbitrary delay; the one exception is described under the courier offline tests. All data is fictional.

## What is covered

### Unit tests: no database

**Domain (81).** 67 are the Week 2 business-rule tests, which **pass unchanged** against the new persistence layer. They cover:
- value objects;
- stock that never goes negative;
- staff eligibility;
- the transaction state machine (pay, void a paid sale, refund);
- money-order verification and masked references;
- weight limits.

`PersistenceReadyDomainTests` cover the Week 3 additions: SKU, category, payment (always more than zero), stock adjustment, and the 30 kg limit.

`IdentityAndCodeTests` (14, final-review round) cover:
- identity by business key for counters, stock rows, transactions and categories;
- opening and closing a counter;
- codes trimmed, upper-cased and printed, and blank codes refused;
- a document number of four characters or fewer reveals nothing;
- an unmasked stored reference is refused;
- a verification recorded for another money-order context is refused.

**Application (109).** Services run against in-memory fake repositories and a `SpyUnitOfWork`. The spy records commits and explicit transactions, can fail a commit, and can cancel one. The tests show:
- **One commit per use case, or none on failure.** Checkout commits the stock, the sale, the lines and the payment once. A failed validation or a failed commit leaves nothing committed.
- **The same business assertions as Week 2.** Only the wiring changed (the spy unit of work), and every Week 2 business assertion is unchanged.
- **Stock adjustments.** There is one transaction per adjustment. A first delivery saves the stock row before the procedure runs. Only a manager of the same branch may adjust. The domain rules run first.
- **Price revision.** The percentage limits are enforced, and the request reaches the set-based port.
- **Audit log access** (`AuditLogServiceTests`, 19):
  - Auditors and Administrators read every branch and the security events;
  - a Branch Manager is pinned to the home branch, and is refused another branch or the security events;
  - a Counter Clerk is refused everything;
  - periods are validated (reversed, more than 366 days), and the page size is clamped.
- **Shipment acceptance** (`ShipmentAcceptanceRuleTests`, 4): a closed branch accepts no shipment; a duplicate shipment ID is refused and the first is kept; rate guards.

### The Week 2 tests after the refactor

The brief asks for the Week 2 business-rule tests to pass unchanged against the new persistence layer. [ADR 0022](adr/0022-week-2-business-rule-tests-in-week-3.md) records the decision. Compared with the last Week 2 commit (`7548441`):

- **Domain:** every Week 2 test file is byte-for-byte unchanged. One file was added (`PersistenceReadyDomainTests`).
- **Application:** services now commit through `IUnitOfWork`, so the test wiring changed: fakes stage changes and a `SpyUnitOfWork` commits. The business assertions are unchanged.
- **Four tests were replaced.** Each tested a Week 2 mechanism that no longer exists. Its business rule is now proven where the rule now lives:

| Week 2 test (removed) | Mechanism it tested | Replaced by |
|---|---|---|
| `Failed_transaction_save_compensates_previously_removed_inventory` | Compensating stock writes after a failed JSON save | `Failed_commit_persists_nothing_and_raises_no_sale_events` (Application); `A_sale_that_fails_part_way_through_the_commit_rolls_back_stock_and_transaction_rows_together` (SQL Server) |
| `Cancelled_transaction_save_still_compensates_previously_removed_inventory` | Compensation ignoring request cancellation | `Cancelled_commit_persists_nothing_and_raises_no_sale_events` (Application) |
| `Reports_group_actual_branch_data_without_identity_details` | The in-memory `BranchReportService` | `The_week_2_report_scenario_gives_the_same_figures_from_sql_server` (SQL Server). It seeds the same scenario and keeps the same ten assertions and expected figures. It also checks that the report type holds counts only, never identity data. |
| `Reports_propagate_cancellation` | Same service | `Daily_sales_include_the_tax_charged_and_every_report_honours_cancellation`, covering all four reports |

### SQL Server integration tests (Infrastructure, 128)

| Area | Tests show |
|---|---|
| Model (`ApexPostModelTests`, no database) | Audit columns on every table; `rowversion` on stock and sales; masked identity column; unique business keys; weight CHECKs; only owned children cascade; decimal(18,2) and varchar codes; mapper round trip; **the workload indexes' key order and INCLUDE columns** |
| Repository mapping (`EfRepositoryMappingTests`) | Every aggregate round-trips with its value objects, owned types and audit columns. Value-object predicates become `varchar(48)` parameters. Duplicate keys become `DuplicateKeyException`, and a detached update is refused. |
| Atomicity and concurrency (`CheckoutPersistenceTests`) | A sale is one commit. **A failure part-way through the commit rolls back stock and sale rows together.** Two clerks selling the last unit leave stock at 0 and one conflict. **8 concurrent sales never oversell or lose an update.** Sales and voids lock stock rows in key order, before the sale rows (the deadlock rule). |
| Stored procedures (`StoredProcedureTests`) | Parameter validation errors. Both report modes. Adjustment and history in one transaction. Refusal below zero. **Only an active Branch Manager of the branch may adjust** (a clerk, another branch's manager and an inactive manager are refused). A procedure update invalidates a stale EF copy. A mixed `SaveChanges` + procedure call rolls back as one unit. **Sales for a period are grouped by day, leave out sales outside the period, and for one day equal the daily procedure.** The period's dates are validated in the database and before the call (reversed, longer than 366 days). **The migrated database runs exactly the `.sql` files in source control.** |
| Migration snapshots (`StoredProcedureMigrationTests`, no database) | Each migration keeps the procedure text it shipped. The latest one for each procedure carries the current script. `Down` restores the previous body, or drops a procedure the migration added. |
| Parallel reports (`ParallelReportTests`) | The four reports run **at the same time**, each on its own context: a gated factory times out unless all four are waiting together. The results equal the sequential reads, for a single day and for a period, and a failing report fails the page with its business message. |
| Query services (`QueryServiceTests`) | Each screen query is **one SQL command** (no N+1) and tracks nothing, including the shipping label and the money-order verification summary (one `LEFT JOIN`). Reports aggregate in the database, count only paid sales of the day, include the tax charged, and honour cancellation. The Week 2 report scenario gives the Week 2 figures. Price revision is one set-based `UPDATE`. |
| Generated SQL (`GeneratedSqlTests`) | Captures `ToQueryString()` for the dashboard, shipping volume and money-order reports (used in `PERFORMANCE.md`), and pins their SARGable shape. Loads sales with their lines as a **single query and as a split query** (ADR 0020): one joined statement against two, with identical results. |
| Checkout constraints (`CheckoutPersistenceTests`) | `CK_Payments_Amount` rejects a payment that is not greater than zero, even from a write that bypasses the domain. |
| Audit store (`EfAuditStoreTests`) | Round trip and staff stamping. A failed business save is audited but never committed by the audit write. A failed audit write does not undo a committed change. |
| Seeding and caching (`SeedingAndRateCacheTests`) | The seed meets the brief's volumes (5 branches, 20 staff, 30 products, 10 zones/60 rates, 240 sales). It refuses an unmigrated database. Rates are cached, but an empty table is not. |
| Audit trail schema (`AuditTrailSchemaTests`, `ApexPostModelTests`) | `DataChanges` and `SecurityEvents` exist with their CHECKs (operation, event type, `ISJSON`, status range) and no foreign keys. The eleven audit indexes have the pinned key order. |
| Data-change history (`DataChangeAuditTests`) | An insert is named by its business key, with the password hash `(hidden)`. An update records only the changed columns, old → new. A sale records its transaction, line, payment and stock in one commit, **with no extra round trip**. A failed save keeps no history, and a retry does not double it. A synchronous save is recorded too. The price revision stays one `UPDATE` and records each product's old and new price. A rolled-back revision leaves neither prices nor history. A stock adjustment records the quantity and its history row; a refused one records nothing. The audit log gets column names, never values. |
| Audit log queries (`AuditLogQueryTests`) | Newest first, a period that includes both days whole and nothing either side, every filter, a page past the end shown as the last page, a trace across all three logs limited to the viewer's branch, and a table filter that lists the business tables but not the audit tables. |
| Tracking tables, Week 4 (`TrackingPersistenceTests`, 17) | A shipment round-trips with its recipient and audit columns. A duplicate tracking number or scan ID is a `DuplicateKeyException`, and a scan needs a known shipment (FK). Two requests that read the same version cannot both save (`rowversion`). A recipient change is **one audited update**. History is oldest first and the view shows the latest scan. The list filters by status and city, ignoring case, and pages by tracking number. **A list page is three statements, and the latest scans are read only for that page's shipments** (measured: 160 instead of 560 logical reads on 30,000 scans). The CHECKs refuse what the domain refuses. **The `apexpost_api` role can do the tracking work and is refused a Week 3 table.** |
| Simulations and tooling (`SimulatedExternalServiceTests`, `DesignTimeFactoryTests`, `CatalogueAndRepositoryGuardTests`) | The courier's concurrency limit, stable status and cancellable wait. The identity verifier's rules. `dotnet ef` reads its connection from the environment and finds every migration in order. Stock levels, and the money-order repository's detached-update guard. |

### Web integration tests (110)

`WebApplicationFactory` hosts the real `Program.cs` against a disposable test database, with cookies and antiforgery tokens handled as a browser would. The tests cover:

- **What the brief requires:**
  - a sale posted through the counter screen is stored with its lines, payment and audit columns;
  - **a void requires the Branch Manager role** (a clerk gets 403);
  - a manager voids a paid sale and the stock is returned;
  - **the reports show the day's totals from the database**, and a chosen from-to period shows its totals and a row per day; a reversed period is explained with a 400;
  - **errors keep their status codes**: 409 for two clerks selling the last unit and for a duplicate email; 404 for a missing sale; 400 for a broken rule or an invalid price revision.
  - **A refused void or refund is shown on the Manage page** with its reason and the 404/400 status. It is not raw Problem Details JSON.
  - **A framework or library `InvalidOperationException` is never shown on a form.** It is a 500, and its text stays private.
- **Over-posting:** posting status, identity, activity, password or audit fields has no effect on a sale or a staff edit.
- **The UI offers only what the role may use:**
  - A clerk's home branch is fixed text plus a hidden field; there is no one-item dropdown.
  - An administrator gets a dropdown of every branch.
  - The left menu shows each role its own screens, and marks exactly one entry as the current page (`aria-current="page"`).
  - Stored values (counters, products, zones) are offered as dropdowns.
  - Opening hours picked as times are stored as text ("Mon-Fri 08:30-17:30; Sat 09:00-12:30") and shown in the pickers again when editing. Closing before opening is refused on the form.
- **Workflows:** a several-line basket; shipments and verified money orders billed in the basket; weight limits and verification errors shown; manager stock adjustment through the procedure; administrator price revision; staff and branch administration.
- **Security pipeline:** sign-in with hashed passwords; inactive staff rejected and existing cookies invalidated; cross-branch denial; security headers and correlation ID; a CSP with no `unsafe-inline`, with every view scanned for inline script or style; Secure-only sign-in and antiforgery cookies outside Development; maintenance mode; API key; IP allowlist; shared integration rate limit; login rate limit; CORS; no exception details in Problem Details or logs; a client abort is not a 500.
- **Audit:**
  - safe fields only;
  - no password, document number or API key in records or logs;
  - an audit-store failure never breaks a committed sale;
  - **the recorded status is the one the client received**, for a sign-in redirect (302), an in-action `Forbid` (403), a mapped exception (404) and a 409 form. This test fails against the old action filter.
- **The complete audit trail** (`AuditTrailTests`, final-review round):
  - **Security events:**
    - sign-ins, sign-outs and failed sign-ins are recorded with the reason and never the password;
    - refused pages, policy refusals and an action's own branch check are `AccessDenied`;
    - a deactivated staff member's session is `SessionRejected` with the reason;
    - rate-limited sign-ins and refused partner requests (missing or wrong key, IP outside the allow-list) are recorded without the key.
  - **Data history from the screens:**
    - an administrator's staff change has its old and new values, a `(hidden)` password and the request's correlation ID;
    - a stock adjustment and a price revision appear in the history.
- **The Audit log screen by role** (`AuditLogScreenTests`):
  - a Counter Clerk has no menu entry and is refused every audit page;
  - a Branch Manager reads their branch's requests and changes, but not security events or another branch;
  - an Auditor reads every list for every branch, including failed sign-ins;
  - an administrator's change shows its old and new values, and its trace ties the request to the change;
  - the CSV export has its header and the filtered rows, and neutralises spreadsheet formulas;
  - a reversed period or an unreadable date is a 400 with the form and a reason;
  - long lists are paged in the theme's colours, and the pages keep to the strict CSP.
- **Branch checks on the label and verification screens** (`AuthenticationTests`): another branch's shipping label and money-order verification are refused; a blank or unknown ID is a 400 or 404, never a 500.
- **Edge paths** (`PartnerApiTests`, `ScreenEdgeCaseTests`, `DemoSeedHostedServiceTests`, `AuditDisplayAndCsvTests`):
  - **Partner API success path:** the parcel-status API with a valid key, its audit row, and a slow courier degrading to "Unavailable" within the timeout. This path was not covered before, because the earlier tests only reached its refusals.
  - **Public pages:** the public and error pages (request ID only, `no-store`).
  - **Stock JSON:** own branch only.
  - **Money orders:** a low-value money order's fee, and a repeated verification refused.
  - **Admin screens:** 404 and duplicate answers, and an administrator who cannot deactivate their own account.
  - **Demo seeder:** never seeds outside Development or without a password.
  - **Audit display and export:** badge colours and CSV formats, including formula neutralisation.

`ProblemDetailsMappingTests` cover each row of the exception mapping, including framework exceptions of the same types staying 500 without detail. Two more tests pin the classification rule: the guard helpers count as business code, but the same exception types thrown inside `List`, `Dictionary`, `string`, `Nullable` or an enumerator called by business code do not. Three more make the rule independent of JIT inlining: a probe controller calls the same business rule fully optimized (inlined) and unoptimized, and both must be 400; exception types the Application declares are 400 without a stack trace; and every controller is scanned for `throw` or `ThrowIf…`, which the rule relies on. CI reruns the error-handling tests with `DOTNET_TieredCompilation=0`.

### Courier offline tests (128)

These cover Week 4 Part A. Every test runs on a real Couchbase Lite database in its own temporary folder, which is deleted afterwards; there are no mocks of the database.

- **Database lifecycle:**
  - `apexpost_courier` opens in the default collection, with its indexes, and data is still there after a reopen;
  - `Dispose` can be called twice, and use after dispose fails;
  - a second open of the same folder is refused while the first is open, but not after a crash.
- **Domain:**
  - the `scan::{Guid}` format and its parsing;
  - `ShipmentScan` validation: tracking number, lengths, UTC time, enums;
  - the correction rules;
  - no Couchbase type in the domain. This is checked by reflection and by a source scan, and a control test proves the check does find Couchbase types in the storage layer.
- **Repository:** key-value create, get, update and delete. A duplicate ID is refused without overwriting, only Pending scans can be created, and list-all is ordered.
- **SQL++:**
  - the three queries;
  - input built to look like an injection attempt, which only ever matches as a value;
  - a check that no query text holds a literal value;
  - `Explain()` showing each query's index with no temporary sort;
  - an older index definition being replaced when the database reopens.
- **Outbox:**
  - success, rejection and offline-then-online runs;
  - batches of 20 and a failure part way through;
  - duplicates after a lost "mark Synced";
  - unanswered scans, and deletes or corrections made during an upload;
  - overlapping runs and cancellation.
- **Change listener:** changed IDs are logged, and the live count goes up on save and down on sync or delete. A disposed monitor hears nothing more, disposing the database removes every listener token, and a failing subscriber does not stop later notifications.
- **Harness:** scripted console sessions drive every menu item end to end, and the listener's log line must appear under the action that caused it.

**The one deliberate delay:** Couchbase Lite raises change notifications on its own thread. Tests wait for an expected notification with a 10-second ceiling, and finish as soon as it arrives. One test proves that something does *not* happen (no notification after dispose), and it waits a fixed 500 ms. The offline tests passed repeated runs in a row on Windows, and CI runs them on Linux.

### Tracking model and API tests (Week 4, Part B)

**Domain (72) and Application (43), no database.**
- **The model:**
  - the tracking number, scan ID and recipient formats, including non-ASCII digits and a trailing line break;
  - the 0.1–70 kg limits;
  - every allowed and refused status move;
  - details editable until finished;
  - delete only while Created.
- **Scan decisions:** the next status moves the shipment, a repeat or late scan is history only, and anything else is refused with its reason.
- **`ShipmentTrackingService`** runs against an in-memory store and the `SpyUnitOfWork`:
  - one commit per command, and none on a refusal;
  - invalid input becomes `RequestValidationException` with the request field's name;
  - a duplicate-key race becomes the same 409 as the up-front check;
  - the upload is idempotent: oldest first, duplicates skipped, another shipment's scan ID rejected without naming it, a concurrent winner turned into a retryable 409.

**API (142, `ApexPost.Api.Tests`).** `WebApplicationFactory` hosts the real `Program.cs`. The endpoint tests share one disposable database per run, migrated at start-up, with the same name guard as the Web tests.

| Area | Tests show |
|---|---|
| Composition (`ApiCompositionTests`) | Minimal hosting starts with every registration validated. The Week 3 `DbContext` has both audit interceptors, and one context is shared per request. The host refuses to start without a connection string. |
| Architecture (`ControllerArchitectureTests`) | Both controllers are `[ApiController]` with attribute routes, depend on the tracking service only, and constrain every `{trackingNumber}`. The 8 endpoints of the brief exist. |
| Endpoints (`ShipmentEndpointTests`) | Each endpoint's success code: **201 with a `Location` that reads the same shipment**, 200 and 204. Then its 400, 404, 409 and 415 answers. Paging and filters; status by name or number. |
| Validation (`RequestValidationTests`, `CustomRuleTests`) | Every DTO rule, **keyed and worded by JSON field names** in `ValidationProblemDetails`. The three custom rules: the phone, the scan time not in the future (with a fixed clock), and a scan ID only once per upload. Unreadable JSON names the path but no internal type. A refused create stores nothing. |
| Errors (`ErrorHandlingTests`) | Production and Staging: **a 500 has no message, type or stack trace**, but the server log has the exception. Development: the 500 carries them. A library `ArgumentException` is 500, not 400. Unknown route, malformed tracking number, 405 and 415 are all ProblemDetails. |
| Scan upload (`ScanUploadEndpointTests`) | Accepted in scan-time order; re-sent → duplicates only; accepted, skipped and rejected (with reason) in one upload; a rejected scan accepted once the shipment is ready. **Parallel identical uploads store each scan once.** A lost race is a 409 that says to send again. Future and malformed scans are 400s naming the scan. |

### OpenAPI, versioning, contracts and client tests (Week 4, Part C)

**API (95 more, `ApexPost.Api.Tests`).** Everything in the OpenAPI documents is checked for **both versions**. Each check was confirmed by a mutation: the code or the document was changed for a moment, the test failed, and the change was undone.

| Area | Tests show |
|---|---|
| Documents (`OpenApiDocumentTests`) | One OpenAPI 3.1 document per API version and no other (`/openapi/v3.json` is a 404). Each has its title, version (1.0, 2.0) and contact, and exactly its version's 8 operations. **v1 opens with its deprecation notice and every v1 operation is `deprecated`**; v2 has neither. The documents describe DTOs, never entities, and are not served outside Development. |
| Metadata (`OpenApiMetadataTests`) | For each operation in each version: its **operation ID, tag, summary and exact status codes**. Every response is described, and every error is `application/problem+json` (`ValidationProblemDetails` for 400). The 201 documents `Location`. Every parameter and field is described; every request field has an example. Enums are named in requests (both versions), numbered in v1 responses and named in v2's. Required request fields are never null; nullable response fields stay nullable. No number is typed as a string. v2 nests the recipient and `latestScan`. |
| "Try it out" (`TryItOutTests`) | For each version, every operation is sent a body built only from the document's own examples, on a real database. Each answer must have a **documented status**, and every success body must have **exactly the documented fields** (including a nested `latestScan`). |
| Swagger UI (`SwaggerUiTests`) | `/swagger` with the API's title, in Development only. The dropdown lists **"v2" then "v1 (deprecated)"**, and each entry loads its document. Swashbuckle's generator is not installed, so there is only one generator. |
| Versioning (`VersioningTests`, `ControllerArchitectureTests`) | v2's shape: a 201 with a v2 `Location`, a nested recipient, names, `latestScan` (the latest by scan time). Both versions show the same shipment. Every response reports the supported and deprecated versions. **v1 carries `Deprecation`, `Sunset` and `Link` with the policy's dates**; v2 does not. The Part B URLs answer as v1, query string included. Unknown versions are 404s. Each controller serves one version, and v1 and v2 have the same routes, operation IDs and requests. v2 never returns a v1 DTO. |
| Contracts (`ContractTests`) | `contracts/v1.json` and `contracts/v2.json` **equal what the API serves**; even a changed XML comment fails the test. The two contracts share their requests and operations and differ in their responses. |
| Generated client (`GeneratedClientTests`, `ClientGenerationTests`) | The console consumer's tour on a real database: create, fetch, two scans, a 404 handled by the generated exception. 404, 400 and 409 arrive as `ApexPostApiException<ProblemDetails>` or `<ValidationProblemDetails>`. Enums, query parameters, nested objects and decimal weights make the round trip. The client has exactly one method per operation in `contracts/v2.json`, says it was generated by the pinned NSwag version, and is registered by `AddApexPostClient` with `AddHttpClient`. |

[`ApexPost.Api.http`](../src/ApexPost.Api/ApexPost.Api.http) was also run, request by request, against the API on a freshly migrated database. All 50 requests (both versions, the documents, an unknown version and the unversioned alias) returned the status code in their titles. The consumer was also run as a real process against a real API: exit code 0, and exit code 1 with a one-line message when the API is down.

## Not covered

- **No browser automation in the suite.** There is no real-browser, screen-reader, keyboard-only or automated accessibility test. The UI round was checked by hand in headless Edge, as each role at desktop and phone widths, with no console or CSP errors. That check is not part of CI.
- **No load or penetration test.** `PERFORMANCE.md` measures query cost with a reproducible data set; it is not a load test.
- **Two real processes on one courier database.** The single-process lock was checked by hand, by starting two harnesses on the same folder (the second exits with code 2). The automated tests check the same lock within one process.
- **The upload race is real, so its interleaving varies.** The parallel-upload tests accept 200 or 409 for each request and always check that no scan was stored twice. In 30 measured rounds, every round produced exactly one 200 and one 409.
- **The `.http` file is not run in CI.** It was run by hand against a fresh database; the endpoint tests cover the same requests.
- **Client regeneration is not run in CI.** CI does not run NSwag. Instead, `ClientGenerationTests` checks that the committed client has one method per operation in the committed contract and comes from the pinned NSwag version. Regenerating with the same contract and tool was checked by hand to give a byte-identical file.
- **Swagger UI's page is checked through its configuration, not in a browser.** The tests read the dropdown configuration and load each document. The "Try it out" behaviour is reproduced over HTTP in `TryItOutTests`.
- **No proxy or multi-host deployment test.** The concurrency tests use several contexts and connections against one SQL Server. That reproduces the conditions of several web servers, but not an actual farm.
