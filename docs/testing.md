# Testing

## Current test projects

The solution contains four xUnit test projects:

- `ApexPost.Domain.Tests` references `ApexPost.Domain`.
- `ApexPost.Application.Tests` references `ApexPost.Application`.
- `ApexPost.Infrastructure.Tests` references `ApexPost.Infrastructure`.
- `ApexPost.Web.IntegrationTests` references `ApexPost.Web`.

`ApexPost.Web.IntegrationTests` references the approved `Microsoft.AspNetCore.Mvc.Testing` package for actual ASP.NET Core test hosting. Domain tests cover value objects/entities; Application tests cover workflows/pricing; Infrastructure tests cover JSON/seed behavior; and Web tests cover MVC, authentication, filters, and middleware. Test data, credentials, correlation IDs, and filesystem locations are synthetic/test-owned.

## Coverage delivered by roadmap steps

Step 7 adds `ApexPost.Infrastructure.Tests`, which references Infrastructure and uses a unique directory below the OS temporary directory for every test. It cleans up only that test-owned directory. Coverage includes round trips for all seven repositories, identity-based update/upsert behavior, missing files, empty/malformed JSON, cancellation, sensitive-data-safe serialization, and concurrent same-process writes.

Step 8 adds seed-data tests using separate test-owned temporary directories. They verify the required 5 branches, 20 staff, 30 products, 10 zones/60 rates, and 40 historical records; role coverage, uniqueness, branch/staff/product/inventory relationships, rate construction through existing value objects, transaction arithmetic, rerun no-op behavior, and rejection of unmarked pre-existing data.

Step 9 adds Application inventory-service tests for available stock, insufficient stock, invalid quantities, branch isolation, positive/negative adjustments, low-stock crossings, and concurrent removals. Infrastructure tests exercise concurrent `JsonInventoryRepository` updates through separate service instances and confirm that exactly the available units are removed without same-process overselling.

Step 10 adds Application shipping-workflow tests for letters and parcels, Standard/Express/Registered services, zones, configured maximum-weight boundaries, missing rates, invalid measurements, persistence errors, cancellation, and distinct missing-origin/shipment handling. Infrastructure tests verify that the JSON rate provider loads the 60 deterministic seed rates and handles missing configuration/cancellation.

Step 11 adds money-order workflow tests for below/at/above configurable thresholds, successful/failed/pending/missing/mismatched-context verification, masked document references, invalid amounts, cancellation, and persistence failures. Repository serialization tests verify that only the masked identity-document reference appears in JSON, not the original full value.

Step 12 adds mixed-counter-transaction tests for retail/service totals and configured tax, insufficient stock, inactive/wrong-branch clerks, closed branches, high-value money-order verification, manager/reason/state-transition constraints, retail-stock restoration on refund, and transaction-save failure compensation.

Step 13 adds report tests for paid-sales grouping/totals, UTC date boundaries, branch isolation, low-stock thresholds, shipping zone/service grouping, privacy-safe money-order verification counts, and cancellation. Notification tests cover high-value money orders, void/refund delivery, and delivery failures after persisted state.

Step 14 replaces the template Web test with deterministic MVC integration tests using `WebApplicationFactory`: all required GET routes render successfully, and a POST with a valid antiforgery token but missing login input returns the validation view. The tests use the application’s local test-host data directory only; no demo data or external service is required.

Step 15 replaces those unauthenticated screen assertions with isolated cookie-authentication integration tests. They seed only test-owned JSON data and use a test-only in-memory password configuration. Coverage verifies unauthenticated login redirects, successful and failed sign-in, inactive-login rejection, cookie invalidation after deactivation, HttpOnly/SameSite cookie settings, cross-branch denial, Counter Clerk denial of privileged reports, and Branch Manager access to branch reports.

Step 16 adds isolated middleware integration tests for security headers and correlation-ID reuse, maintenance-mode `503` with `/health` bypass, integration API-key missing/configuration-failure responses, IP allowlist denial, login `429` rate limiting, and approved/unapproved CORS preflight behavior. The in-memory test configuration uses only test keys and temporary, test-owned data directories. TestServer does not populate a remote client IP for manager POSTs, so those endpoints are intentionally denied by the new IP gate in that host; the integration endpoint independently verifies the required `403` behavior.

Step 17 adds JSON audit-store concurrency coverage, verifying that simultaneous same-process appends preserve every record. MVC integration tests verify a successful authenticated action records staff identity, role, branch, action metadata, status, and a supplied correlation ID; invalid-model login returns the validation view and is audited as failed; the submitted password is absent from the JSON audit file; and a zero-millisecond performance threshold produces an MVC-action warning. Test records, credentials, identifiers, and correlation IDs are synthetic. Cross-process append coordination and tamper resistance are intentionally not claimed or tested because the JSON store does not provide them.

## Step 18 test strategy and coverage

The suite is deterministic and layered. Domain tests cover local aggregate/value-object invariants. Application tests use controlled in-memory implementations to verify stock cannot become negative, high-value money orders require a verified matching transaction context, shipping rejects a weight above its selected service limit, pricing rejects invalid/missing/ambiguous rates, and branch/staff/transaction authorization rules remain enforced. They also cover cancellation and representative persistence/notification error paths.

Infrastructure tests isolate every JSON test under a new temporary directory, including same-process concurrent append/update behavior and sensitive-data-safe serialization. Web tests use `WebApplicationFactory` with synthetic in-memory configuration, local temporary data, and test-only passwords. Required integration cases cover security headers, `401` for missing/invalid integration API keys, `403` for a configured-blocked IP, deterministic fixed-window login `429` after exactly the active configured permitted requests, protected-route login redirect, Clerk `403` for void, and echoed/generated `X-Correlation-Id`.

No implementation gap remains among the required cases for endpoints currently implemented. The known environmental limitation is that TestServer does not model a realistic remote client IP for the manager-reversal path; the endpoint is therefore denied by the real IP gate in that host. The independently blocked-IP integration-endpoint test validates the allowlist behavior. Cross-process JSON durability/tamper resistance also remains outside the guarantees of this project.

## Step 19 concurrency and asynchronous test evidence

Concurrency tests verify that concurrent same-process inventory removals and checkouts cannot oversell stock, concurrent JSON audit appends preserve every record, and JSON locking protects read-modify-write operations. The audit-store tests now also prove cancellation is reported as `OperationCanceledException` without creating a record, while malformed JSON is a separate `JsonException` failure. Existing Application/Infrastructure tests cover cancellation propagation for shipping, money orders, reports, repositories, and transaction checkout; notification-delivery failures are asserted separately from persistence outcomes.

The checkout validation fan-out uses `Task.WhenAll` only for branch/counter/clerk/duplicate-ID reads, which target independent aggregates. Tests retain serialized assertions for stock and transaction mutations; no test assumes parallel writes. No benchmark is recorded because the repository has no supported benchmark harness or representative load data. Cache and `ValueTask` tests are intentionally absent because neither optimization is implemented: there is no measured repeated-read hot path that justifies cache invalidation complexity, and existing async operations are genuinely I/O-bound `Task` operations rather than commonly synchronous completions.

The roadmap coverage above is implemented and retained as a record of the delivered test scope. Additional tests should be added when future features or defects warrant them; all new tests should continue to verify the assignment rules in [business-rules.md](business-rules.md) and security constraints in [security.md](security.md).

## Step 22 architecture regression coverage

Integration rate limiting uses a shared bounded partition and does not trust `X-Partner-Id`. A WebApplicationFactory regression test sends 61 valid-key requests with distinct partner-header values against the configured 60-request limit and verifies the final request receives `429` instead of opening a new limiter partition. API-key failures occur before this limit and do not consume its quota. This is an aggregate per-process integration limit, not a per-partner quota or a distributed limit.

The Step 22 full solution run completed with 128 passed, 0 failed, and 0 skipped across Domain (39), Application (55), Infrastructure (17), and Web integration (17).

## Step 20 delivery verification

The documented full command is:

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build --verbosity normal
```

The most recent Step 20 validation is 127 passed, 0 failed, 0 skipped. Required web integration coverage includes security headers, API-key `401`, blocked-IP `403`, rate-limit `429`, unauthenticated redirect, Clerk void `403`, and correlation-ID response. Seed tests verify the documented 5 branches, 20 staff, 30 products, 10 zones/60 rates, and 40 historical snapshots; 150 inventory records follow from five branches times 30 products. There is no browser screenshot, Postman collection, benchmark harness, external provider, or cross-process JSON test to present as a delivery artifact.

## Step 23 security and reliability regressions

Application tests cancel transaction persistence after stock has been removed and verify the compensation completes despite request cancellation. Infrastructure tests force JSON replacement to fail by making the target path a directory, then verify the existing target remains and the sibling temporary file is removed. Web tests verify unexpected exception details are excluded from Problem Details and application logs, correlation ID and exception type are retained, request-abort cancellation is recorded as internal status 499 without attempting a response body, and synthetic passwords, document numbers, and API keys are absent from request logs and audit records.

Existing integration coverage continues to exercise wrong-branch access, inactive-staff cookie rejection, Clerk denial of void, API-key configuration/missing/invalid cases, blocked-IP behavior, rate limits, maintenance mode, CORS, correlation IDs, headers, and safe audit fields. These tests use synthetic values and temporary test-owned data directories. No penetration test, proxy deployment test, cross-process JSON test, or production certification is claimed.

The required validation commands are:

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build --verbosity normal
git diff --check
```

Step 23 full solution validation completed with 132 passed, 0 failed, and 0 skipped: Domain (39), Application (56), Infrastructure (19), and Web integration (18).

## Step 24 MVC workflow and delivery coverage

Web integration tests seed test-owned branch, counter, product, inventory, shipping-rate, and synthetic staff records in temporary data directories. Coverage verifies branch claim defaults and role-aware navigation, explicit wrong-branch denial for managers, Clerk denial of both reversal endpoints, successful checkout redirect/confirmation with stock reduction, antiforgery rejection without inventory mutation, configured shipping weight-limit feedback, high-value money-order verification feedback, and empty states across all reports for a branch with no activity. Shipping-volume and money-order reports are branch aggregates, not filtered by the selected sales day. The test host is not a persistent demo seed mechanism.

MVC GET routes are exercised through WebApplicationFactory, along with login validation, protected-route redirects, cookie invalidation, and branch restrictions. Tests assert important view content and effects, but they do not execute a real browser, screen reader, keyboard-only audit, axe scan, or WCAG conformance test. Manual accessibility compliance is therefore unverified. No authenticated reversal UI, transaction history/lookup screen, or supported developer seed CLI exists; tests do not fabricate those flows.

The reproducible test-host demo command is:

```powershell
dotnet test tests/ApexPost.Web.IntegrationTests/ApexPost.Web.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AuthenticationTests"
```

This exercises synthetic checkout, shipping eligibility, money-order verification, and empty report states. It does not establish that a fresh developer can run the same flows interactively: the production app currently has no supported command to populate its persistent data directory, and no real/demo passwords are committed.

Step 24 full solution validation completed with 140 passed, 0 failed, and 0 skipped: Domain (39), Application (56), Infrastructure (18), and Web integration (27). The solution build completed with 0 warnings and 0 errors.

## Commands

From the solution root:

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build
```
