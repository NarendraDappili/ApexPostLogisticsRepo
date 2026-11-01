# ApexPost Logistics

ApexPost is a .NET 10 ASP.NET Core MVC application for post-office counters:
- retail sales, shipments, and money orders with identity verification;
- voids and refunds;
- branch stock;
- branch reports;
- staff and branch administration.

Data lives in **Microsoft SQL Server** (`ApexPostDb`), accessed through **EF Core 10** with code-first migrations.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- SQL Server 2019 or later: Developer, Express or LocalDB. It was developed on SQL Server 2025, and CI uses the SQL Server 2022 container image.
- Optional: `sqlcmd` or SQL Server Management Studio, to run the scripts in [`/sql`](sql/) and [`/database`](database/).

The EF Core command-line tool (`dotnet-ef` 10.0.13) is pinned in [`dotnet-tools.json`](dotnet-tools.json). `dotnet tool restore` installs exactly that version for this repository; no global install is needed. Run all commands from the repository root.

## Quick start

```powershell
.\scripts\setup-dev.ps1                      # tool restore, connection-string secret, create and migrate ApexPostDb
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
- the three stored procedures;
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

Every seeded account uses the Development password **`ApexPost-Demo-2026`**. It is set in `appsettings.Development.json` as `ApexPost:DemoSeed:Password`; only per-user PBKDF2 hashes are stored. The data is fictional, and the password is never used outside Development.

| Role | Staff ID | Home branch |
|---|---|---|
| Counter Clerk | `STF-CLERK-01-A`, `STF-CLERK-01-B` … `STF-CLERK-05-B` | BR-NORTH (01) … BR-CENTRAL (05) |
| Branch Manager | `STF-MANAGER-01` … `STF-MANAGER-05` | BR-NORTH … BR-CENTRAL |
| Auditor | `STF-AUDITOR-01` … `STF-AUDITOR-03` | BR-NORTH, BR-SOUTH, BR-EAST |
| Administrator | `STF-ADMIN-01`, `STF-ADMIN-02` | BR-WEST, BR-CENTRAL |

Seeded values you can use: products `PRD-001` … `PRD-030` (`PRD-001`–`PRD-003` start at low stock), counters `C-01` and `C-02` at every branch, and zones `ZONE-01` … `ZONE-10`.

### Suggested walkthrough

1. **Shipment.** Sign in as `STF-CLERK-01-A`. Under **Shipping**, create shipment `SHP-DEMO-1` for transaction ID `TX-DEMO-1`, zone `ZONE-01`, 2 kg. The label shows the charge from the rate table.
2. **Money order.** Under **Money orders**, issue `MO-DEMO-1` for 1500.00 with transaction ID `TX-DEMO-1`.
   - The fee is calculated (2.50 + 1%), and you are taken to the verification step.
   - Choose **Passport** and enter `AB123456`. The simulated verifier returns *Verified*, and only `****3456` is stored. Numbers ending in `0000` simulate a rejection.
3. **Sale.** Under **Counter basket**, use transaction ID `TX-DEMO-1`, counter `C-01`, a retail row (for example `PRD-004` × 2), shipment `SHP-DEMO-1` and money order `MO-DEMO-1`, then pay. Every price is calculated on the server, and the sale, its lines, its payment and the stock decrement are saved in one transaction.
4. **Manager.** Sign in as `STF-MANAGER-01`.
   - Under **Transactions**, void or refund `TX-DEMO-1` with a reason; its stock is returned.
   - Under **Catalogue**, adjust stock for a product (it runs `usp_AdjustStock` and records the history).
   - Open **Reports** for daily sales (retail vs services), low stock, shipping volume, and money orders with their verifications.
5. **Administrator.** Sign in as `STF-ADMIN-01`.
   - Under **Staff** and **Branches**, add, edit, deactivate and reactivate.
   - Under **Catalogue**, revise a category's prices by a percentage (one set-based `UPDATE`).

## Tests

```powershell
dotnet build ApexPost.slnx -c Release -warnaserror
dotnet test ApexPost.slnx -c Release --no-build
```

**What the tests need:**
- The Infrastructure and Web tests need a SQL Server. Each test run creates its own database (`ApexPostDb_Test_<guid>`) from the migrations and drops it afterwards. The development database is never used, and a guard refuses any other database name.
- By default the tests use the local default instance with Windows authentication. Point them elsewhere with `APEXPOST_TEST_SQLSERVER`, a connection string without a database name:

  ```powershell
  $env:APEXPOST_TEST_SQLSERVER = "Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True"
  ```

**Results:** the latest run had 261 tests passing (Domain 66, Application 86, Infrastructure 59, Web integration 50) and a zero-warning build.

**CI:** [`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds with warnings as errors, checks that every model change has a migration, starts a throwaway SQL Server container with a random password, and runs every test on each push and pull request to `master`.

## Database artifacts

| What | Where |
|---|---|
| Migrations | [`src/ApexPost.Infrastructure/Persistence/Migrations`](src/ApexPost.Infrastructure/Persistence/Migrations) |
| Idempotent schema script | [`database/ApexPostDb.idempotent.sql`](database/ApexPostDb.idempotent.sql) (`dotnet ef migrations script --idempotent`) |
| Stored procedures (`CREATE OR ALTER`) | [`src/ApexPost.Infrastructure/Persistence/StoredProcedures`](src/ApexPost.Infrastructure/Persistence/StoredProcedures), deployed by the migrations |
| Least-privilege role | [`database/security/apexpost-app-role.sql`](database/security/apexpost-app-role.sql) |
| Reporting queries, atomic T-SQL sale, blocking demo | [`sql/`](sql/) |
| Seeding service | [`src/ApexPost.Infrastructure/Seed/DemoDataSeeder.cs`](src/ApexPost.Infrastructure/Seed/DemoDataSeeder.cs) |
| Performance data set and measurement scripts | [`database/perf/`](database/perf/) |

## Configuration

`appsettings.json` holds non-secret defaults only. The settings live under `ApexPost`:

| Setting | Purpose |
|---|---|
| `DemoSeed:Enabled`, `DemoSeed:Password` | Development seeding (on in `appsettings.Development.json`) |
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

- [DESIGN.md](DESIGN.md): architecture, layer diagrams, ER diagram, normalisation, data access patterns, SOLID mapping, all ADRs.
- [PERFORMANCE.md](PERFORMANCE.md): execution plans, before/after measurements, index trade-offs, generated SQL.
- [sql/README.md](sql/README.md): reporting queries, the atomic T-SQL sale, the blocking demo, deadlock avoidance.
- [docs/security.md](docs/security.md), [docs/testing.md](docs/testing.md), [docs/business-rules.md](docs/business-rules.md), [docs/architecture/README.md](docs/architecture/README.md).
- [AUDIT-SAMPLE.json](AUDIT-SAMPLE.json): the shape of an audit record.

## Known limitations

- **Simulated services.** Notifications are log entries; identity verification and courier tracking are simulated. There is no payment processing, MFA, account lockout or password rotation.
- **Audit is best-effort.** An audit write failure is logged and never undoes a committed sale (ADR 0011).
- **IP allowlist and proxies.** The allowlist uses the direct connection address. Behind a reverse proxy, configure trusted forwarded headers first.
