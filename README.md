# ApexPost Logistics

ApexPost is a .NET 10 ASP.NET Core MVC application for post-office counters:
- retail sales, shipments, and money orders with identity verification;
- voids and refunds;
- branch stock;
- branch reports;
- staff and branch administration.

Data lives in **Microsoft SQL Server** (`ApexPostDb`), accessed through **EF Core 10** with code-first migrations.

Week 4 adds the courier side. **Part A** is an offline store for the courier device: shipment scans saved as JSON documents in an embedded **Couchbase Lite** database, with an outbox that uploads them later ([details below](#courier-offline-store-week-4-part-a), [OFFLINE-DESIGN.md](OFFLINE-DESIGN.md)). **Part B** is **ApexPost.Api**, a REST API for tracked shipments and the idempotent upload of those scans, on the same SQL Server database ([details below](#tracking-api-week-4-part-b), [API-NOTES.md](API-NOTES.md)). **Part C** documents that API with OpenAPI and Swagger UI and versions it: v1 keeps the Part B contract and is deprecated, and v2 is current. It also adds **ApexPost.Client**, a typed C# client generated from the committed contract ([details below](#api-documentation-openapi-and-swagger-ui-week-4-part-c), [VERSIONING.md](VERSIONING.md)).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- SQL Server 2019 or later: Developer, Express or LocalDB. It was developed on SQL Server 2025, and CI uses the SQL Server 2022 container image.
- Optional: `sqlcmd` or SQL Server Management Studio, to run the scripts in [`/sql`](sql/) and [`/database`](database/).

The EF Core command-line tool (`dotnet-ef` 10.0.13) is pinned in [`dotnet-tools.json`](dotnet-tools.json). `dotnet tool restore` installs exactly that version for this repository; no global install is needed. Run all commands from the repository root.

## Quick start

```powershell
.\scripts\setup-dev.ps1                      # tool restore, connection string and demo password in user-secrets, create and migrate ApexPostDb
dotnet run --project src/ApexPost.Web        # seeds the demo data on the first start
```

[`scripts/setup-dev.ps1`](scripts/setup-dev.ps1) defaults to the local default instance with Windows authentication. For LocalDB, pass `-ConnectionString "Server=(localdb)\MSSQLLocalDB;Database=ApexPostDb;Trusted_Connection=True"`. It never overwrites a connection string you have already set. The steps below explain what it does, and how to do each one by hand.

## 1. Set the connection string (user-secrets)

The connection string is never committed. In Development it comes from user-secrets. The web project already has a `UserSecretsId`, so do **not** run `dotnet user-secrets init`.

```powershell
# Local default instance, Windows authentication
dotnet user-secrets set --project src/ApexPost.Web "ConnectionStrings:ApexPostDb" "Server=.;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True"

# Or LocalDB
dotnet user-secrets set --project src/ApexPost.Web "ConnectionStrings:ApexPostDb" "Server=(localdb)\MSSQLLocalDB;Database=ApexPostDb;Trusted_Connection=True"
```

`dotnet ef` reads the same secret, so migrations target the database the app uses. Outside Development, supply `ConnectionStrings__ApexPostDb` through the deployment's secret store or an environment variable. If the setting is missing, the app refuses to start and tells you so.

The partner integration API key is a secret too:

```powershell
dotnet user-secrets set --project src/ApexPost.Web "ApexPost:Security:Integration:ApiKey" "<local-integration-key>"
```

Without it, `/integration/parcel-status` returns 500 ("not configured") by design.

## 2. Create ApexPostDb and apply the migrations

```powershell
dotnet tool restore
dotnet ef database update --project src/ApexPost.Infrastructure
```

This creates `ApexPostDb` if it does not exist and applies every migration:
- tables, keys and constraints;
- the four stored procedures;
- the workload indexes.

EF Core creates the database with `READ_COMMITTED_SNAPSHOT ON`, which the application expects: readers never wait for writers (see [sql/README.md](sql/README.md)). The application itself never creates or migrates the schema.

**If a DBA creates the database instead,** use the reviewed idempotent script:

```powershell
sqlcmd -S . -E -Q "CREATE DATABASE ApexPostDb; ALTER DATABASE ApexPostDb SET READ_COMMITTED_SNAPSHOT ON;"
sqlcmd -S . -E -d ApexPostDb -I -i database\ApexPostDb.idempotent.sql
```

`-I` turns on `QUOTED_IDENTIFIER`. `sqlcmd` leaves it off by default, and the filtered index on staff email needs it. The script can be run again safely.

**For a shared or production server,** give the application's login only the least-privilege role in [database/security/apexpost-app-role.sql](database/security/apexpost-app-role.sql): no `DELETE`, no DDL, and an append-only audit trail. Run migrations under a separate deployment login.

## 3. Run

```powershell
dotnet run --project src/ApexPost.Web
```

Open http://localhost:5145 (or https://localhost:7163 with the `https` profile).

On the first start in Development, an **empty, migrated** database is seeded with fictional demo data:
- 5 branches with 2 counters each;
- 20 staff;
- 30 products in 3 categories, stocked at every branch;
- 10 zones with 60 shipping rates;
- 240 sales over the last 60 days, with their shipments, money orders and identity verifications.

The seed is saved in one transaction. A database that already has data is never touched. If migrations are pending, the seeder logs the `dotnet ef database update` command to run and seeds nothing.

To start again from scratch:

```powershell
dotnet ef database drop --project src/ApexPost.Infrastructure
dotnet ef database update --project src/ApexPost.Infrastructure
```

## Demo accounts

Every seeded account uses the Development demo password: the user-secret `ApexPost:DemoSeed:Password`. No password is committed.
- `scripts/setup-dev.ps1` generates a random one and prints it. See it again with `dotnet user-secrets list --project src/ApexPost.Web`.
- Or choose your own before the first run: `dotnet user-secrets set --project src/ApexPost.Web "ApexPost:DemoSeed:Password" "<at least 10 characters>"`.

Only per-user PBKDF2 hashes are stored. The data is fictional, and the seeder never runs outside Development.

| Role | Staff ID | Home branch |
|---|---|---|
| Counter Clerk | `STF-CLERK-01-A`, `STF-CLERK-01-B` … `STF-CLERK-05-B` | BR-NORTH (01) … BR-CENTRAL (05) |
| Branch Manager | `STF-MANAGER-01` … `STF-MANAGER-05` | BR-NORTH … BR-CENTRAL |
| Auditor | `STF-AUDITOR-01` … `STF-AUDITOR-03` | BR-NORTH, BR-SOUTH, BR-EAST |
| Administrator | `STF-ADMIN-01`, `STF-ADMIN-02` | BR-WEST, BR-CENTRAL |

Seeded values you can use: products `PRD-001` … `PRD-030` (`PRD-001`–`PRD-003` start at low stock), counters `C-01` and `C-02` at every branch, and zones `ZONE-01` … `ZONE-10`.

## Using the application

- **Navigation:** the left menu shows only the screens your role may use, grouped into Overview, Counter, Inventory, Management, Administration and Audit. The current screen is highlighted. On a phone, open the menu with the button at the top left.
- **Top bar:** shows where you are (breadcrumb), your home branch, your role, and Sign out.
- **Branch:** clerks and branch managers work at their home branch only, so it is shown as fixed text marked "Your home branch". Administrators and auditors may view every branch, so they choose it from a dropdown.
- **Forms:** codes that refer to stored records (counters, products, zones, payment methods, roles) are dropdowns, so you pick a valid value instead of typing one.
- **Dates and times:** reports take a From and a To date from date pickers. A branch's opening hours are picked as times for Monday to Friday, Saturday and Sunday (leave both empty for a closed day) and saved as text such as "Mon-Fri 09:00-17:30; Sat 09:00-12:30".
- **Lists:** in the catalogue, a product at or below the low-stock threshold (`Inventory:LowStockThreshold`, the same value the dashboard and reports use) gets a highlighted "Low stock" badge; the row itself keeps the normal list colours. Staff and branch lists show each row's status and its actions side by side.
- **Audit log:** under **Audit**, three tabs:
  - **Requests:** every screen request, with its status;
  - **Data changes:** every row inserted, updated or deleted, with its old and new values;
  - **Security:** sign-ins, sign-outs and refusals.

  Each tab has filters, paging and a CSV export, and a correlation ID opens a trace of one request across all three. Auditors and administrators see every branch and all three tabs. Branch managers see their own branch's requests and data changes. Clerks have no access. See [ADR 0024](docs/adr/0024-complete-audit-trail-and-audit-log-screen.md).
- **Look:** yellow, red and black (about 50/30/20), with the logo as a faint watermark. All styling is one stylesheet (`wwwroot/css/site.css`), so it works under the strict Content Security Policy. See [ADR 0023](docs/adr/0023-application-shell-and-theme-under-the-strict-csp.md).

### Suggested walkthrough

1. **Shipment.** Sign in as `STF-CLERK-01-A`. Under **Shipping**, create shipment `SHP-DEMO-1` for transaction ID `TX-DEMO-1`, zone `ZONE-01`, 2 kg. The label shows the charge from the rate table.
2. **Money order.** Under **Money orders**, issue `MO-DEMO-1` for 1500.00 with transaction ID `TX-DEMO-1`.
   - The fee is calculated (2.50 + 1%), and you are taken to the verification step.
   - Choose **Passport** and enter `AB123456`. The simulated verifier returns *Verified*, and only `****3456` is stored. Numbers ending in `0000` simulate a rejection.
3. **Sale.** Under **Counter basket**, use transaction ID `TX-DEMO-1`, counter `C-01`, a retail row (for example `PRD-004` × 2), shipment `SHP-DEMO-1` and money order `MO-DEMO-1`, then pay. Every price is calculated on the server, and the sale, its lines, its payment and the stock decrement are saved in one transaction.
4. **Manager.** Sign in as `STF-MANAGER-01`.
   - Under **Transactions**, void or refund `TX-DEMO-1` with a reason; its stock is returned.
   - Under **Catalogue and stock**, adjust stock for a product (it runs `usp_AdjustStock` and records the history).
   - Open **Reports** for sales (retail vs services) on a day or over a from-to period, with a row per day, plus low stock, shipping volume, and money orders with their verifications. The quick links pick today, the last 7 or 30 days, or this month.
5. **Administrator.** Sign in as `STF-ADMIN-01`.
   - Under **Staff** and **Branches**, add, edit, deactivate and reactivate.
   - Under **Catalogue and stock**, revise a category's prices by a percentage (one set-based `UPDATE`).
6. **Auditor.** Sign in as `STF-AUDITOR-01`, and open **Audit log**.
   - **Data changes** shows the sale from step 3, the stock it took (`Quantity` old → new), the void, the adjustment and the repriced products. A password change shows as `(hidden)`.
   - **Security** shows the sign-ins and sign-outs of the walkthrough. A wrong password shows as `SignInFailed` with the reason `WrongPassword`.
   - Select a correlation ID to see everything that one request recorded.
   - The console log shows the same records under the `ApexPost.Audit` category, without the values.

## Courier offline store (Week 4, Part A)

Couriers record pickups and deliveries without a network. Each scan is saved first on the device, in an embedded **Couchbase Lite 4.1** database, and uploaded later. Why it works this way, how conflicts are handled and why the database belongs to one process are explained in **[OFFLINE-DESIGN.md](OFFLINE-DESIGN.md)**. The Week 3 SQL Server code is not involved and was not changed.

| Project | Contents |
|---|---|
| [`ApexPost.Courier.Offline`](src/ApexPost.Courier.Offline) | `Domain`: `ShipmentScan`, `ScanId`, `IScanRepository`, `IShipmentUploader`, with no Couchbase types (a test enforces this). `Storage`: `CourierDatabase`, `CouchbaseScanRepository`, `ScanChangeMonitor`, the SQL++ queries. `Sync`: `OutboxService`, `FakeShipmentUploader`. |
| [`ApexPost.Courier.Offline.Harness`](src/ApexPost.Courier.Offline.Harness) | The console test harness |
| [`ApexPost.Courier.Offline.Tests`](tests/ApexPost.Courier.Offline.Tests) | 128 tests on real temporary databases; no SQL Server or network needed |

**Run the harness** (no Couchbase Server, Sync Gateway or network needed):

```powershell
dotnet run --project src/ApexPost.Courier.Offline.Harness                          # courier CR-001, data in %LOCALAPPDATA%\ApexPost\CourierOffline
dotnet run --project src/ApexPost.Courier.Offline.Harness -- --courier CR-007 --data .\courier-data
```

On an empty device it offers five fictional sample scans. The menu is **Add Scan**, **Find by Tracking Number**, **List Pending**, **Simulate Sync**, **Delete Scan** and **Exit**. The header above the menu shows the live pending count and the courier's scans per type. **Simulate Sync** asks whether the network is available: answer `n` and every scan stays Pending; answer `y` and they are uploaded to the fake server. That server rejects a scan time more than 5 minutes in the future and reports a re-sent scan ID as a duplicate. Starting a second harness on the same data folder is refused, because the database is single-process.

**Document model.** The database is `apexpost_courier`, and scans live in its default collection. Each document's key is `scan::{Guid}` (a version 7 GUID, so newer IDs sort later):

```json
{
  "trackingNumber": "APX-24000001",
  "scanType": "Pickup",
  "courierId": "CR-001",
  "location": "Riverside Depot",
  "scannedAtUtc": "2026-11-05T05:22:41.1234567Z",
  "notes": "Collected from front desk",
  "syncStatus": "Pending"
}
```

Enums are stored by name. `scannedAtUtc` is fixed-width UTC text, so sorting the text gives time order. Reads and writes by ID are key-value operations (`GetDocument` / `Save` on the key), and a new scan's save fails instead of overwriting an existing ID.

**SQL++ queries and indexes.** The queries are in [`ScanQueries.cs`](src/ApexPost.Courier.Offline/Storage/ScanQueries.cs). Their text is constant, and every value is a `$parameter`; a test fails if any query contains a quoted value. Each index starts with the filtered field. Its second key is the sort or grouping field, so `Explain()` shows an index search with no temporary sort, and a test checks that:

| Query | SQL++ | Index | Plan (`Explain()`) |
|---|---|---|---|
| One parcel's history, oldest first | `WHERE trackingNumber = $trackingNumber ORDER BY scannedAtUtc` | `idx_scans_trackingNumber` (trackingNumber, scannedAtUtc) | `SEARCH _ USING INDEX idx_scans_trackingNumber (<expr>=?)` |
| Pending scans | `WHERE syncStatus = $syncStatus ORDER BY scannedAtUtc` (value `Pending`) | `idx_scans_syncStatus` (syncStatus, scannedAtUtc) | `SEARCH _ USING INDEX idx_scans_syncStatus (<expr>=?)` |
| Scans per type for one courier | `SELECT scanType, COUNT(*) AS scanCount … WHERE courierId = $courierId GROUP BY scanType` | `idx_scans_courierId_scanType` (courierId, scanType) | `SEARCH _ USING INDEX idx_scans_courierId_scanType (<expr>=?)` |

With single-field indexes, the first two queries used their index but still sorted the results in a temporary B-tree, and the courier count read every document (`SCAN _`). The third index is an addition to the two the brief requires; each index costs a little on every save.

**Change listener.** `ScanChangeMonitor` registers a collection change listener. Each notification logs the changed document IDs and recounts the Pending scans (`Scan documents changed: scan::…; pending scans: 3`), and the harness header shows that live count. `CourierDatabase` keeps every listener token and removes them all in `Dispose()` before it closes the database.

**Outbox.** `OutboxService.SyncPendingAsync()` uploads the Pending scans, oldest first, in batches of 20 through `IShipmentUploader`. The server's answer marks each scan: accepted or duplicate becomes Synced, rejected becomes Failed with the reason. A failed upload leaves the remaining scans Pending, and runs never overlap. In Part D, the real API client replaces `FakeShipmentUploader`.

**Screenshots** (taken from the harness) are in [`docs/screenshots/week4-partA`](docs/screenshots/week4-partA):
- the query results: one parcel's history, the pending list, and the per-type counts in the header;
- the listener log after adding, syncing and deleting scans.

## Tracking API (Week 4, Part B)

`ApexPost.Api` is an ASP.NET Core Web API for tracked shipments and their courier scans. It uses minimal hosting (no `Startup.cs`). Through dependency injection it reuses the Week 3 `ApexPostDbContext`, unit of work, audit interceptors and error types. **[API-NOTES.md](API-NOTES.md)** has the endpoint and status-code tables, the validation rules, the error format and how the scan upload stays idempotent.

| Project | Contents |
|---|---|
| [`ApexPost.Api`](src/ApexPost.Api) | `Program.cs`. `Controllers/V1` and `Controllers/V2`: `ShipmentsController`, `ScansController`. `Contracts`: request DTOs and v1's response DTOs; `Contracts/V2`: v2's response DTOs. `Validation`: the custom rules. `Errors/ApiExceptionHandler`. `Routing`: the `{trackingNumber:trackingNumber}` constraint. `OpenApi` and `Versioning` (Part C). [`ApexPost.Api.http`](src/ApexPost.Api/ApexPost.Api.http) |
| Week 3 projects (additions only) | Domain: `TrackedShipment`, `TrackingScan`, `TrackingStatusRules`, `TrackingNumber`, `TrackingScanId`, `Recipient`. Application: `ShipmentTrackingService`, `ITrackedShipmentRepository`, `ITrackingQueries`. Infrastructure: their EF Core configuration, repository and queries, plus the migration `AddShipmentTracking` |
| [`ApexPost.Api.Tests`](tests/ApexPost.Api.Tests) | 237 tests:<br>• every endpoint of both versions over HTTP on a disposable SQL Server database;<br>• the error handling in Production and Development;<br>• the validation rules;<br>• the OpenAPI documents and Swagger UI;<br>• the committed contracts;<br>• the generated client and its console consumer |

**Run it:**

```powershell
dotnet ef database update --project src/ApexPost.Infrastructure      # once: adds TrackedShipments and TrackingScans
dotnet run --project src/ApexPost.Api --launch-profile http          # http://localhost:5245
```

It reads the same `ConnectionStrings:ApexPostDb` user-secret as the web application, so there is nothing else to set up. Then send the requests in [`ApexPost.Api.http`](src/ApexPost.Api/ApexPost.Api.http) from Visual Studio or VS Code (REST Client), top to bottom. Each endpoint has one success and at least one failure, for both versions. All 50 requests were run against a fresh database and returned the codes in their titles.

**Endpoints** (`{version}` is `2`, or `1` for the deprecated Part B contract; see [Versions](#api-documentation-openapi-and-swagger-ui-week-4-part-c)):

| Verb | Route | Success |
|---|---|---|
| GET | `/api/v{version}/shipments?page=&pageSize=&status=&city=` | 200 (page size 20 by default, at most 50) |
| GET | `/api/v{version}/shipments/{trackingNumber}` | 200 |
| POST | `/api/v{version}/shipments` | 201 with `Location` |
| PUT | `/api/v{version}/shipments/{trackingNumber}` | 200 |
| PATCH | `/api/v{version}/shipments/{trackingNumber}/status` | 200 |
| DELETE | `/api/v{version}/shipments/{trackingNumber}` | 204, only while Created |
| POST | `/api/v{version}/shipments/{trackingNumber}/scans` | 200 with `accepted`, `duplicatesSkipped` and `rejected` (with reasons) |
| GET | `/api/v{version}/shipments/{trackingNumber}/scans` | 200, oldest first |

**Errors and validation:**
- **Every error is ProblemDetails:** 400, 404, 405, 409, 415 and 500.
- **Validation** runs before any action: `[ApiController]` checks the request DTOs and returns a 400 `ValidationProblemDetails`. There are three custom rules: a dialable phone number, a scan time not in the future, and no repeated scan ID in one upload.
- **The global `IExceptionHandler`** maps by exception type only: 400, 404 and 409 for the application's typed errors, and 500 for anything else. A 500 shows its stack trace only in Development ([ADR 0026](docs/adr/0026-api-errors-by-exception-type.md)).

**Why a new tracking model:** the Week 3 `Shipment` is a counter parcel. It has no recipient, status or scans, and a 30 kg limit. Tracked shipments are a new aggregate added by a migration that only creates tables, so the counter application is unchanged ([ADR 0025](docs/adr/0025-tracked-shipments-beside-counter-shipments.md)).

## API documentation: OpenAPI and Swagger UI (Week 4, Part C)

In Development the API describes itself and offers a page for trying it. `dotnet run --project src/ApexPost.Api --launch-profile http` opens that page in the browser.

| URL | What it serves |
|---|---|
| `http://localhost:5245/openapi/v2.json` | The OpenAPI document for v2 |
| `http://localhost:5245/openapi/v1.json` | The OpenAPI document for v1. It is marked deprecated: its description opens with the deprecation and sunset dates, and every operation has `deprecated: true` |
| `http://localhost:5245/swagger` | Swagger UI. Its document dropdown lists "v2" (opened first) and "v1 (deprecated)" |

**The OpenAPI specification, the built-in .NET generator and Swagger UI are three different things:**

- **The OpenAPI specification is a format, not a tool.** It is a vendor-neutral standard for describing an HTTP API in JSON or YAML. A description covers:
  - its paths, verbs and parameters;
  - its request and response schemas;
  - its status codes;
  - who to contact.

  A document written to it (ours is OpenAPI 3.1) is the API's contract. Anyone can read it, and tools can generate a client from it or compare two versions of it without seeing our code.
- **The built-in .NET generator writes our documents.** It is `Microsoft.AspNetCore.OpenApi`, with `AddOpenApi()` and `MapOpenApi()`. At run time it reads the controllers, routes, DTOs and attributes through ASP.NET Core's API explorer. It produces one document per API version, `/openapi/v1.json` and `/openapi/v2.json`. Each document holds the API Explorer group of the same name, and Asp.Versioning names those groups "v1" and "v2" ([`OpenApiRegistration`](src/ApexPost.Api/OpenApi/OpenApiRegistration.cs)). A document transformer, [`ApiDocumentInfoTransformer`](src/ApexPost.Api/OpenApi/ApiDocumentInfoTransformer.cs), adds the title "ApexPost Logistics API", the description, the version and the team's contact (fictional). For v1 it adds the deprecation notice, taken from the same versioning policy as v1's `Deprecation` and `Sunset` headers. The generator produces JSON only; it has no user interface.
- **Swagger UI only displays the documents.** It is a static HTML and JavaScript page that reads OpenAPI documents and renders them as interactive documentation. Its "Try it out" button sends real requests to the API. We install only the viewer package, `Swashbuckle.AspNetCore.SwaggerUI`, and point it at the built-in documents, one dropdown entry per API version. Swashbuckle's own generator (SwaggerGen) is not installed, so there is only one generator; a test checks this.

In short: the specification is the format, the generator writes our API's document in that format, and Swagger UI shows that document to people.

**What the document contains:**

- **Operations:**
  - **Operation IDs:** every action has a stable route name, such as `CreateShipment` or `UploadScans`. The route name becomes the operation's `operationId` and the method name in a generated client, so it is never renamed.
  - **Tags and summaries:** `[Tags("Shipments")]` and `[Tags("Scans")]` group the operations, and `[EndpointSummary]` gives each one a one-line summary.
  - **Response codes:** `[ProducesResponseType]` declares every status an operation can return, each with its body type and a description of when it happens. Errors are `application/problem+json` (`ValidationProblemDetails` for a 400).
  - **201:** a 201 lists its `Location` header.
- **XML comments:** `GenerateDocumentationFile` is on, and a public member without a comment fails the build. The comments on actions and DTOs become the descriptions, and each request field has an `<example>`, so "Try it out" opens with a valid body.
- **The contract, not the C# types:** [`ContractTransformers.cs`](src/ApexPost.Api/OpenApi/ContractTransformers.cs) corrects the places where the C# types would mislead a client:
  - an enum that travels as a name is a named string enum;
  - a v1 response's numeric status lists its numbers;
  - numbers are typed as numbers, not "number or string";
  - a `[Required]` request field is never nullable.

  A test sends every operation the document's own examples on a real database. Each answer must have a documented status, and each success body must have exactly the documented fields.

**Versions.** The version is part of the URL: `/api/v1/shipments` and `/api/v2/shipments`. Both versions call the same `ShipmentTrackingService` and take the same requests; only their response DTOs and mapping differ.

| | v1 (deprecated) | v2 |
|---|---|---|
| Response shape | The Part B contract, unchanged: flat recipient fields | `recipient { name, phone, address, city }`, plus `latestScan { scanType, location, scannedAtUtc }` (null until the first scan) |
| Enums in responses | Numbers (`"status": 2`) | Names (`"status": "InTransit"`) |
| Headers | `api-supported-versions: 2.0`, `api-deprecated-versions: 1.0`. Successful responses also carry `Deprecation` (since 9 November 2026) and `Sunset` (31 May 2027), with `Link` headers to the versioning policy | `api-supported-versions`, `api-deprecated-versions` |

The unversioned Part B URLs (`/api/shipments…`) still answer as v1 until v1's sunset, so Part B clients keep working. A `Location` header always gives the versioned URL.

**Generated C# client.** [`ApexPost.Client`](src/ApexPost.Client) is a typed client for v2. NSwag generates it from the committed contract [`contracts/v2.json`](contracts/v2.json).

| File | Role |
|---|---|
| [`contracts/export-contracts.ps1`](contracts/export-contracts.ps1) | Writes `contracts/v1.json` and `contracts/v2.json` from the running API. Works in Windows PowerShell and `pwsh` |
| [`src/ApexPost.Client/nswag.json`](src/ApexPost.Client/nswag.json) | The exact NSwag settings. The NSwag version is pinned in [`dotnet-tools.json`](dotnet-tools.json) |
| `src/ApexPost.Client/Generated/ApexPostClient.g.cs` | The generated client, committed and never edited by hand |
| `ApexPostClient.Partial.cs` | Hand-written additions in partial classes: `TryGetShipmentAsync`, `IsNotFound`, `IsConflict` |
| `ClientRegistration.cs` | A wrapper: `AddApexPostClient(baseAddress)` registers the client with `AddHttpClient` |

```powershell
./contracts/export-contracts.ps1                  # after an API change: refresh the contracts, review their diff
dotnet tool restore
dotnet nswag run src/ApexPost.Client/nswag.json   # regenerate the client from contracts/v2.json
```

The console consumer [`ApexPost.Client.Consumer`](src/ApexPost.Client.Consumer) registers the client with `AddHttpClient` and then:
1. creates a shipment;
2. fetches it;
3. uploads two scans;
4. fetches it again to see its new status and latest scan;
5. asks for a shipment that does not exist, catching the 404 as the generated `ApexPostApiException<ProblemDetails>`.

Start the API, then run `dotnet run --project src/ApexPost.Client.Consumer` (the address is `ApexPostApi:BaseAddress` in its `appsettings.json`).

**Contract tracking.** [`contracts/v1.json`](contracts/v1.json) and [`contracts/v2.json`](contracts/v2.json) are the published contracts.
- `ContractTests` fails when the API no longer produces exactly these documents, so every contract change, even to a description, arrives as a reviewed diff of those files.
- **[VERSIONING.md](VERSIONING.md)** lists each change between v1 and v2, whether it is breaking and why, and v1's deprecation and sunset plan.
- [ADR 0027](docs/adr/0027-url-segment-api-versioning-with-sunset.md) and [ADR 0028](docs/adr/0028-openapi-contracts-and-generated-client.md) record the decisions.

**Screenshots:** [docs/screenshots/week4-partC/README.md](docs/screenshots/week4-partC/README.md) lists the ones to take: Swagger UI with v1 and v2, a 400, a 409, and a 201 with `Location`.

**Development only.** Outside Development, both URLs return 404. This decides what we publish; it is not security. Hiding the description protects no endpoint, and every endpoint answers the same either way (authentication comes in Part D). `/health` is left out of the document because it is a probe for the hosting platform, not part of the client contract.

## Tests

```powershell
dotnet build ApexPost.slnx -c Release -warnaserror
dotnet test ApexPost.slnx -c Release --no-build
```

**What the tests need:**
- The courier offline tests (`ApexPost.Courier.Offline.Tests`) need nothing installed: each test opens its own Couchbase Lite database in a temporary folder.
- The Infrastructure, Web and API tests need a SQL Server. Each test run creates its own database (`ApexPostDb_Test_<guid>`, or `ApexPostDb_ApiTest_<guid>` for the API) from the migrations and drops it afterwards. The development database is never used, and a guard refuses any other database name.
- By default the tests use the local default instance with Windows authentication. Point them elsewhere with `APEXPOST_TEST_SQLSERVER`, a connection string without a database name:

  ```powershell
  $env:APEXPOST_TEST_SQLSERVER = "Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True"
  ```

**Results:** the latest run had 925 tests passing and a zero-warning build:

| Project | Tests |
|---|---|
| Domain | 153 |
| Application | 152 |
| Infrastructure | 145 |
| Web integration | 110 |
| Courier offline | 128 |
| API (with the OpenAPI, versioning, contract and generated-client tests) | 237 |

Line coverage of the Week 3 projects is 97% (see [docs/testing.md](docs/testing.md#line-coverage)).

**CI:** [`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds with warnings as errors, checks that every model change has a migration, starts a throwaway SQL Server container with a random password, and runs every test on each push and pull request to `master`, then reruns the error-handling tests with full JIT optimization. Every run on GitHub Actions so far has passed; [docs/testing.md](docs/testing.md#ci-on-github-actions) links them.

## Database artifacts

| What | Where |
|---|---|
| Migrations | [`src/ApexPost.Infrastructure/Persistence/Migrations`](src/ApexPost.Infrastructure/Persistence/Migrations) |
| Idempotent schema script | [`database/ApexPostDb.idempotent.sql`](database/ApexPostDb.idempotent.sql) (`dotnet ef migrations script --idempotent`) |
| Stored procedures (`CREATE OR ALTER`) | [`src/ApexPost.Infrastructure/Persistence/StoredProcedures`](src/ApexPost.Infrastructure/Persistence/StoredProcedures), deployed by the migrations |
| Least-privilege roles | [`database/security/apexpost-app-role.sql`](database/security/apexpost-app-role.sql) (web application); [`database/security/apexpost-api-role.sql`](database/security/apexpost-api-role.sql) (tracking API: the two tracking tables and the audit table only) |
| Reporting queries, atomic T-SQL sale, blocking demo | [`sql/`](sql/) |
| Seeding service | [`src/ApexPost.Infrastructure/Seed/DemoDataSeeder.cs`](src/ApexPost.Infrastructure/Seed/DemoDataSeeder.cs) |
| Performance data set and measurement scripts | [`database/perf/`](database/perf/) |

## Configuration

`appsettings.json` holds non-secret defaults only. The settings live under `ApexPost`:

| Setting | Purpose |
|---|---|
| `DemoSeed:Enabled`, `DemoSeed:Password` | Development seeding (on in `appsettings.Development.json`); the password is a user-secret only |
| `Inventory:LowStockThreshold`, `Inventory:LowStockRecipient` | Low-stock alerts and reports |
| `MoneyOrders:HighValueThreshold`, `FlatFee`, `FeeRate` | Identity-verification threshold and fee |
| `Transactions:TaxRate` | Tax on sales |
| `Courier:*` | Simulated courier lookup: timeout, latency, concurrency |
| `Performance:WarningThresholdMilliseconds` | Slow-action warning |
| `Maintenance`, `RateLimiting` | Maintenance mode; login and integration limits |
| `Security:Integration:AllowedIps`, `Security:PartnerTrackingWidgetOrigin` | IP allowlist (loopback in Development, empty elsewhere); partner CORS origin |
| `DataDirectory` | Local folder for the ASP.NET Core data-protection keys only (ignored by Git) |

In Development, EF Core logs every SQL command (`Microsoft.EntityFrameworkCore.Database.Command`), so you can see that each screen sends one query.

Sample integration request:

```powershell
curl -H "X-Api-Key: <local-integration-key>" "http://localhost:5145/integration/parcel-status?shipmentId=SHP-0001"
```

## Documentation

- [DESIGN.md](DESIGN.md): architecture, layer diagrams, ER diagram, normalisation, data access patterns, user interface, SOLID mapping, all ADRs.
- [OFFLINE-DESIGN.md](OFFLINE-DESIGN.md): the courier offline store: save locally first, conflict handling, one process per database.
- [API-NOTES.md](API-NOTES.md): the tracking API: endpoints and status codes, validation, errors, the idempotent scan upload.
- [VERSIONING.md](VERSIONING.md): the tracking API's versions: every change from v1 to v2, breaking or not and why, v1's deprecation and sunset plan, how to change the contract. The contracts are in [contracts/](contracts).
- [PERFORMANCE.md](PERFORMANCE.md): execution plans, before/after measurements, index trade-offs, generated SQL.
- [sql/README.md](sql/README.md): reporting queries, the atomic T-SQL sale, the blocking demo, deadlock avoidance.
- [docs/security.md](docs/security.md), [docs/testing.md](docs/testing.md), [docs/business-rules.md](docs/business-rules.md), [docs/architecture/README.md](docs/architecture/README.md).
- [AUDIT-SAMPLE.json](AUDIT-SAMPLE.json): the shape of an audit record.

## Known limitations

- **Simulated services.** Notifications are log entries; identity verification and courier tracking are simulated. There is no payment processing, MFA, account lockout or password rotation.
- **Request and security audit are best-effort.** Such an audit write failure is logged and never undoes a committed sale (ADR 0011). Data-change history is written in the same transaction as the change, so it is never lost for a committed change (ADR 0024).
- **Audit history is never pruned.** Nothing deletes audit rows; retention is a deployment decision.
- **IP allowlist and proxies.** The allowlist uses the direct connection address. Behind a reverse proxy, configure trusted forwarded headers first.
- **The tracking API has no authentication yet.** Anyone who can reach it can call every endpoint. Part D adds JWT bearer tokens with roles, and API keys for partners; until then, run it only locally.
- **v1 of the tracking API is deprecated.** It answers until 31 May 2027, and so do the unversioned Part B URLs (`/api/shipments…`), which are an alias of v1. New clients should use `/api/v2`.
- **Counter and tracked shipments are not linked.** A parcel accepted at the counter does not get a tracking number automatically ([ADR 0025](docs/adr/0025-tracked-shipments-beside-counter-shipments.md)).
