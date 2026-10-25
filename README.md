# ApexPost Logistics

ApexPost Logistics is a .NET 10 MVC application for a postal, shipping, and retail-counter workflow. It uses JSON persistence in this assignment implementation; no database, ORM, external identity provider, or external notification provider is included.

## Prerequisites and installation

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PowerShell or another shell capable of running the .NET CLI

Clone the repository, then run all commands below from the solution root. No package other than the declared Microsoft/xUnit packages is required.

## Restore, build, test, and run

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build --verbosity normal
dotnet run --project src/ApexPost.Web
```

The most recently verified suite contains 128 passing tests, with 0 failures and 0 skipped tests. The web host prints its assigned URL; use HTTPS outside local development.

## Configuration and local secrets

`ApexPost:DataDirectory` defaults to `App_Data` under the web application's content root. It contains operational JSON files and ASP.NET Core data-protection keys, is ignored by Git, and should be protected with ordinary OS access controls.

Committed `appsettings.json` contains configuration shapes and non-secret local defaults only. Initialize user secrets for a local checkout before adding a demo password or integration key:

```powershell
dotnet user-secrets init --project src/ApexPost.Web
dotnet user-secrets set --project src/ApexPost.Web "ApexPost:Authentication:DemoAccounts:STF-CLERK-01-A" "<local-demo-password>"
dotnet user-secrets set --project src/ApexPost.Web "ApexPost:Security:Integration:ApiKey" "<local-integration-key>"
```

Do not commit those values. Other active keys include `ApexPost:Inventory:LowStockThreshold`, `ApexPost:MoneyOrders:HighValueThreshold`, `ApexPost:Transactions:TaxRate`, `ApexPost:Performance:WarningThresholdMilliseconds`, maintenance settings, rate limits, partner origin, and integration IP allowlist.

There are no configured demo login accounts or passwords in source control. The deterministic seed creates synthetic staff in Counter Clerk, Branch Manager, Auditor, and Administrator roles, but login works only after a matching local secret is configured. Seeded names and identifiers are synthetic.

## Implemented features

- Validated Domain value objects and entities for branches, counters, staff, products, inventory, shipments, money orders, and counter transactions.
- JSON repositories with asynchronous I/O, same-process file locking, full-document replacement writes, and cancellation propagation.
- Branch inventory operations, low-stock notifications, shipping/rate validation, money-order identity verification, mixed checkout, manager void/refund, reports, and simulated notifications.
- MVC/Razor workflows with antiforgery, cookie authentication, role/branch authorization, health endpoint, integration parcel-status endpoint, rate limiting, CORS, correlation IDs, security headers, maintenance mode, and safe Problem Details.
- Global MVC audit, validation, and performance filters. `audit-records.json` stores safe metadata only; see [AUDIT-SAMPLE.json](AUDIT-SAMPLE.json).

## Architecture and quality review

The project references follow the intended dependency direction: Domain has no project dependencies; Application references Domain; Infrastructure references Application and Domain; Web references Application and Infrastructure; and each test project references only its subject layer. `Program.cs` is the Web composition root and pipeline configuration.

Integration requests now share a single bounded fixed-window rate-limit partition, and API-key validation precedes integration rate limiting. The limiter no longer creates partitions from caller-controlled `X-Partner-Id` values, preventing arbitrary header values from bypassing the configured limit and growing limiter state; invalid credentials do not consume the shared quota. A WebApplicationFactory regression test covers the configured aggregate limit. No business rules, persistence guarantees, or approved package dependencies changed.

## Demo seed data

`DemoDataSeeder` is not invoked by web startup. Against an empty or nonexistent data directory, it writes synthetic data verified by Infrastructure tests:

| Data | Count |
| --- | ---: |
| Branches | 5 |
| Counters | 10 |
| Staff | 20 |
| Products | 30 |
| Inventory records | 150 |
| Destination zones | 10 |
| Shipping rates | 60 |
| Historical transaction snapshots | 40 |

The seeder writes `apexpost-demo-seed.json` last. A matching manifest makes a later seed call a no-op; an unmarked non-empty directory is rejected without modification. There is no automatic reset/reseed command. To reset a local demo, stop the app and remove only the explicitly chosen local data directory after confirming it contains no data or keys to retain; then invoke the seeder from a trusted local utility or test harness. Do not point it at production data.

## Known limitations and gaps

- JSON writes are coordinated only within one process. Multi-file workflows are not database transactions; a crash or failed compensation can require reconciliation.
- JSON audit append is application-level append-only, not tamper-proof, WORM, cross-process safe, or durable-ledger storage.
- Notifications are local logging, not external delivery. No external identity verification, payment processing, MFA, account lockout, password rotation, or production session revocation is implemented.
- Shipping rates are read from JSON without a measured cache. No external lookup exists, so no provider timeout is implemented.
- No screenshots, Postman collection, or reproducible successful integration sample response is supplied; this repository intentionally does not fabricate them.

## Documentation

- [Architecture and ADRs](DESIGN.md)
- [Business rules and implementation status](docs/business-rules.md)
- [Security and audit constraints](docs/security.md)
- [Test strategy and verified coverage](docs/testing.md)
- [Synthetic audit schema example](AUDIT-SAMPLE.json)
