# ApexPost Logistics

ApexPost Logistics is a .NET 10 solution for a maintainable postal, shipping, and retail-counter platform. It is being built incrementally from the assignment specification; this repository currently contains the Step 7 solution architecture, domain value objects and core entities, Application contracts, configurable pricing rules, JSON repositories, and template application.

## Status

**Completed**

- Seven-project solution with the intended layer and test-project boundaries.
- .NET 10, C# 14, nullable-reference-type, and implicit-using configuration.
- Validated immutable domain value objects for identifiers, monetary amounts, and parcel measurements, with focused xUnit coverage.
- Core domain entities for branches, counters, staff, catalogue products, inventory, shipments, and money orders, with local invariant coverage.
- Application-layer repository, audit, notification, and current-staff-context contracts, ready for future consumers and Infrastructure implementations.
- Deterministic retail and shipping pricing calculators using configurable in-memory rate data; configuration loading remains planned.
- Minimal ASP.NET Core MVC template application and placeholder application/web test projects.
- Architecture, business-rule, security, and testing documentation.

**In progress**

- No business functionality is in progress as part of this step.

**Planned**

- Transaction composition, tax, void/refund authorization, and other cross-entity business workflows.
- JSON-based pricing configuration loading and persistence.
- Application services and business workflows that consume the contracts.
- Dependency-injection registrations, MVC workflows, authentication, and integration tests.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Solution structure

```text
ApexPost.slnx
src/
  ApexPost.Domain/            Value objects and core entities; workflows planned
  ApexPost.Application/       Contracts and configurable pricing calculators; services planned
  ApexPost.Infrastructure/    Technical adapters and JSON repository persistence
  ApexPost.Web/               ASP.NET Core MVC composition root and UI template
tests/
  ApexPost.Domain.Tests/      Domain tests
  ApexPost.Application.Tests/ Application tests
  ApexPost.Web.IntegrationTests/ Web integration tests
```

## Architecture

Dependencies flow inward: Web depends on Application and Infrastructure; Infrastructure depends on Application and Domain; Application depends on Domain; Domain has no solution-project dependencies. Each test project references only its corresponding subject project.

See [DESIGN.md](DESIGN.md) for the architecture and planned request flow. Supporting requirements are documented in [business rules](docs/business-rules.md), [security](docs/security.md), and [testing](docs/testing.md).

## Build and test

Run these commands from the solution root:

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build
```

## Step 7 JSON persistence

`ApexPost.Infrastructure` implements the seven existing Application repository interfaces with asynchronous JSON files; no database or ORM is used. Create repositories with `JsonRepositoryOptions { DataDirectory = "<application-data-directory>" }`. The directory is configuration, not a secret, and is intentionally not wired into the template web app yet.

Files below that directory are `branches.json`, `counters.json`, `staff-members.json`, `products.json`, `inventory.json`, `shipments.json`, and `money-orders.json`. Missing files behave as empty repositories. Empty or malformed JSON and filesystem failures are reported to the caller and are not overwritten.

## Step 8 demo data

`DemoDataSeeder` in Infrastructure creates deterministic, synthetic local demonstration data through the JSON repositories: 5 branches, 10 counters, 20 staff, 30 products, 150 inventory records, 10 zones with 60 shipping rates, and 40 historical retail records. It is intentionally not invoked from `Program.cs`; a future local composition step may opt in by constructing it with the same `JsonRepositoryOptions` used for persistence.

Call `await new DemoDataSeeder(options).SeedAsync(cancellationToken)` only with an empty, local data directory. A completed `apexpost-demo-seed.json` manifest makes a rerun a no-op. Any non-empty directory without the matching manifest is rejected without modification, preventing accidental overwrite of user data. The generated people, identifiers, and transaction references are synthetic. No passwords, secrets, identity-document references, or real personal data are seeded.

## Step 9 inventory behavior

`InventoryService` in Application provides branch-specific available-stock lookup, positive stock additions/removals, and non-zero signed adjustments. A removal or negative adjustment that exceeds available stock fails without persisting a change. Construct it with `IInventoryRepository`, `InventoryServiceOptions` (non-negative low-stock threshold and recipient), and optionally the existing `INotificationSender`; no delivery adapter is implemented here.

The JSON repository performs every inventory mutation inside its per-file in-process lock, preventing same-process read–modify–write overselling across service/repository instances. It does not coordinate multiple application processes sharing a directory. Low-stock notification is awaited only when a change crosses from above the threshold to at/below it; a delivery failure is surfaced after the stock change is already persisted.

## Step 10 shipping workflow

`ShippingWorkflowService` validates raw shipment input through Domain value objects, confirms the origin branch exists and is open, loads rates through `IShippingRateProvider`, uses the existing `ShippingPriceCalculator`, and asynchronously saves through `IShipmentRepository`. `JsonShippingRateProvider` loads `shipping-rates.json` from `JsonRepositoryOptions.DataDirectory`; the Step 8 seed supplies 60 deterministic rates across 10 zones, both shipment types, and all three service levels.

The configured rate supplies the only implemented maximum weight and fixed price. Weight equal to that maximum is accepted; unsupported or ambiguous rates and excessive weights fail before persistence. The current Domain `Shipment` requires positive dimensions for both letters and parcels, so the workflow requires them for both rather than inventing letter defaults or parcel-size limits. There is no courier integration, label rendering, transaction workflow, or MVC screen in this step.

## Step 11 money orders and identity verification

`MoneyOrderWorkflowService` creates money orders with amount, synthetic sender/recipient data, fee, and a transaction-context ID. Its `IMoneyOrderPolicy` supplies the high-value threshold explicitly; this repository does not prescribe a production threshold. Orders strictly above that configured amount require a `Verified` identity result with the same context ID before persistence. Orders at or below the threshold do not require verification.

An identity request’s full document number is used only in memory to derive a masked reference (`****` plus up to four trailing characters). JSON persistence stores document type, outcome, and the masked reference only—never the supplied document number. No identity-provider integration, login credentials, MVC screen, or counter-transaction workflow is introduced here.

## Step 12 mixed counter transactions

`CounterTransactionWorkflowService` creates paid mixed baskets containing retail lines, priced service lines, and existing money-order service lines. Retail pricing uses `RetailPriceCalculator`; shipping and other service prices are inputs from their owning workflows, so checkout does not recreate pricing formulas. `ITaxPolicy` supplies an explicit tax calculation; `FixedRateTaxPolicy` is available only when its rate is supplied at composition time.

Checkout requires an open branch and counter plus an active Counter Clerk assigned to that branch. It serializes checkout/reversal work per branch in-process, removes retail stock through atomic inventory updates, then saves the paid transaction. If the transaction save fails, it restores already-removed stock in reverse order and reports any compensation failure. JSON files cannot offer a database transaction: a process crash, cross-process writer, or failed compensation can still require reconciliation. Void requires an active assigned Branch Manager and reason while open; refund requires the same authorization and reason while paid and restores retail stock after its status save. A restoration failure is explicitly surfaced.

## Step 13 reports and simulated notifications

`BranchReportService` provides branch-scoped daily sales (retail/service/tax), low stock, shipping volume by zone/service level, and money-order verification counts. Report outputs contain counts, totals, product IDs, zones, and verification outcomes only; sender, recipient, document type, and masked identity data are excluded. Low-stock reports accept an explicit non-negative threshold from their caller.

`INotificationSender` now receives simulated low-stock crossing alerts, successful high-value-money-order alerts, and every successful void/refund alert. Delivery is awaited after the related state is persisted; failures are propagated and do not roll back that persisted state. No external notification provider is implemented.

## Step 14 MVC and Razor Views

Run the local web application with `dotnet run --project src/ApexPost.Web`. The default conventional route is `https://localhost:<assigned-port>/`; `dotnet run` prints the actual launch URL. The MVC screens cover a clearly marked login UI placeholder, branch dashboard, catalogue/branch stock, counter basket, shipment creation and label preview, money order/identity-verification form, and branch reports. Authentication and authorization are deliberately not implemented, so the screens must not be treated as access control.

`ApexPost:DataDirectory` defaults to the local `src/ApexPost.Web/App_Data` directory. It holds JSON data and local ASP.NET Core data-protection keys for antiforgery; it is ignored by Git. It starts empty. For synthetic demo data, call the existing `DemoDataSeeder` explicitly against that empty directory before using data-dependent screens; the web app never overwrites or seeds existing data automatically. The shown threshold and tax settings are local development configuration, not production policy.
