# ApexPost Logistics

ApexPost Logistics is a .NET 10 MVC application for a postal, shipping, and retail-counter workflow. It uses JSON persistence behind repository interfaces; no database, ORM, external identity provider, or external notification provider is included.

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

The most recently verified suite contains 186 passing tests, with 0 failures, 0 skipped tests and a zero-warning build. The GitHub Actions workflow in [.github/workflows/ci.yml](.github/workflows/ci.yml) runs the same build (with warnings as errors) and tests on every push and pull request to `master`.

## Demo data and logins

On the first `dotnet run` in Development, the app seeds a synthetic data set into `src/ApexPost.Web/App_Data` before it accepts requests: 5 branches, 10 counters, 20 staff, 30 products with stock, 10 zones with 60 shipping rates, and 40 historical transactions (with their shipments and money orders) over the last four days, so every report has data. The folder is runtime data and is ignored by Git. To start again from a fresh data set, stop the app and delete `src/ApexPost.Web/App_Data`. If the folder already contains non-demo data, seeding is skipped with a log warning and nothing is changed.

Every seeded account uses the Development demo password **`ApexPost-Demo-2026`** (set in `appsettings.Development.json` as `ApexPost:DemoSeed:Password`; only its PBKDF2 hash is stored).

| Role | Staff ID | Home branch |
| --- | --- | --- |
| Counter Clerk | `STF-CLERK-01-A` (also `-01-B`, `-02-A` … `-05-B`) | BR-NORTH (01) … BR-CENTRAL (05) |
| Branch Manager | `STF-MANAGER-01` … `STF-MANAGER-05` | BR-NORTH … BR-CENTRAL |
| Auditor | `STF-AUDITOR-01` … `STF-AUDITOR-03` | BR-NORTH, BR-SOUTH, BR-EAST |
| Administrator | `STF-ADMIN-01`, `STF-ADMIN-02` | BR-WEST, BR-CENTRAL |

Useful seeded values: products `PRD-001` … `PRD-030` (`PRD-001`–`PRD-003` start at low stock), counters `C-01` and `C-02` at every branch, zones `ZONE-01` … `ZONE-10`.

### Suggested walkthrough

1. Sign in as `STF-CLERK-01-A`. Under **Shipping**, create shipment `SHP-DEMO-1` for transaction ID `TX-DEMO-1`, zone `ZONE-01`, 2 kg. The label shows the configured charge.
2. Under **Money orders**, issue `MO-DEMO-1` for 1500.00 with transaction ID `TX-DEMO-1`. The fee is calculated (2.50 + 1%) and you are taken to the verification step. Choose **Passport** and enter `AB123456`; the simulated verifier returns *Verified* and stores `****3456` only. (Numbers ending in `0000` simulate a rejection.)
3. Under **Counter basket**, use transaction ID `TX-DEMO-1`, counter `C-01`, add up to five retail rows (for example `PRD-004` × 2), shipment ID `SHP-DEMO-1` and money-order ID `MO-DEMO-1`, and pay. Every price is calculated on the server.
4. Sign in as `STF-MANAGER-01`. Under **Transactions**, void or refund `TX-DEMO-1` with a reason; its retail stock is returned. Open **Reports** for the daily sales split, low stock, shipping volume and money-order/verification counts.
5. Sign in as `STF-ADMIN-01`. Under **Staff** and **Branches**, add, edit, deactivate/reactivate staff and add, edit, close/reopen branches.

## Configuration and local secrets

Committed `appsettings.json` contains configuration shapes and non-secret defaults only. The Web project already has a `UserSecretsId`; do not run `dotnet user-secrets init` again. The partner integration API key is never committed; set it locally with User Secrets:

```powershell
dotnet user-secrets set --project src/ApexPost.Web "ApexPost:Security:Integration:ApiKey" "<local-integration-key>"
```

Without it, `/integration/parcel-status` returns 500 ("not configured") by design. Use your deployment platform's secret manager in production.

Other settings under `ApexPost`: `DataDirectory`, `DemoSeed:Enabled`/`Password` (Development only), `Inventory:LowStockThreshold`, `MoneyOrders:HighValueThreshold`/`FlatFee`/`FeeRate`, `Transactions:TaxRate`, `Courier:LookupTimeoutMilliseconds`/`LatencyMilliseconds`/`MaxConcurrentLookups`, `Performance:WarningThresholdMilliseconds`, `Maintenance`, `RateLimiting`, `Security:PartnerTrackingWidgetOrigin` and `Security:Integration:AllowedIps`. The IP allowlist (admin screens and the integration endpoint) is empty outside Development; Development allows loopback (`127.0.0.1`, `::1`) only.

### Sample integration request

```powershell
curl -H "X-Api-Key: <local-integration-key>" "http://localhost:5145/integration/parcel-status?shipmentId=SHP-001"
```

Response shape: `shipmentId`, `originBranch`, `destinationZone`, `serviceLevel`, `shipmentType`, `courierStatus`, `courierLookupTimedOut`. A slow courier lookup times out into `courierStatus: "Unavailable"` rather than failing the request.

## Implemented features

- Validated Domain value objects and entities for branches (region, opening hours, counters), staff (email, role, home branch, password hash), products, inventory, shipments, money orders, and counter transactions.
- Staff and branch management screens for Administrators (add, edit, deactivate/close, reactivate/reopen).
- Mixed counter basket with several retail lines, shipments and money orders, all priced on the server; manager void (of a paid sale) and refund, both returning stock.
- Money-order fee from configuration and a separate identity-verification step whose outcome is decided by the (simulated) verifier; only masked document references are stored.
- PBKDF2-hashed staff passwords, cookie authentication with role and branch policies, antiforgery, rate limiting, CORS, correlation IDs, security headers, maintenance mode, health endpoint and safe Problem Details.
- Events (`TransactionCompleted`, `TransactionVoided`, `TransactionRefunded`, `StockLow`, `MoneyOrderIssued`) with a notification observer; low-stock alerts are raised by sales.
- Global MVC audit, validation and performance filters. The audit log is append-only JSON Lines (`audit-records.jsonl`) holding safe metadata only; see [AUDIT-SAMPLE.json](AUDIT-SAMPLE.json).
- Four branch reports: daily sales split by retail and services, low stock, shipping volume by zone and service level, money orders and verifications.

## Known limitations

- JSON writes are coordinated only within one process. Multi-file workflows are not database transactions; a crash or failed compensation can require reconciliation.
- The JSON Lines audit log is append-only but not tamper-proof, WORM, or cross-process safe.
- Notifications are local log entries; identity verification and courier tracking are simulated. No payment processing, MFA, account lockout or password rotation policy is implemented.
- New branches created in the admin screens have counters but no stock rows; stock adjustment screens are not part of this release.
- Performance improvements (rate cache, parallel validation reads) are explained in DESIGN.md but have not been benchmarked.

## Documentation

- [Architecture overview](docs/architecture/README.md)
- [Detailed design and ADRs](DESIGN.md)
- [Business rules and implementation status](docs/business-rules.md)
- [Security and audit constraints](docs/security.md)
- [Test strategy and verified coverage](docs/testing.md)
- [Synthetic audit schema example](AUDIT-SAMPLE.json)
