# Testing

## Current test projects

The solution contains three xUnit test projects:

- `ApexPost.Domain.Tests` references `ApexPost.Domain`.
- `ApexPost.Application.Tests` references `ApexPost.Application`.
- `ApexPost.Web.IntegrationTests` references `ApexPost.Web`.

`ApexPost.Web.IntegrationTests` also references the approved `Microsoft.AspNetCore.Mvc.Testing` package for future ASP.NET Core test hosting. Domain Tests cover value-object construction, normalization, invalid inputs, decimal precision, and value equality. They also cover branch and counter state, staff activity and branch assignment, product price updates, non-negative inventory, shipment composition, money-order verification outcomes, and entity identity equality. Application Tests now cover retail quantity calculations and configured shipping-rate selection, all three service levels, shipment type and zone selection, maximum-weight boundaries, missing/ambiguous rates, and invalid service levels. The shipping test rate values are controlled fixtures, not production configuration. Web Tests retain their template test.

## Planned coverage

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

Later steps should add focused unit tests for domain invariants and application use cases, plus web integration tests for relevant HTTP behavior. Tests should verify the assignment rules described in [business-rules.md](business-rules.md) and security-sensitive behavior described in [security.md](security.md) as those features are implemented.

## Commands

From the solution root:

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build
```
