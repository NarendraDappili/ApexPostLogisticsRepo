# ADR 0013: Relational schema design, keys and delete behaviours

Status: Accepted. Supersedes the storage part of ADR 0002.

## Context

Week 3 replaces the JSON files with SQL Server as the system of record. The schema has to:

- hold the existing domain model without weakening its rules;
- be normalised (3NF);
- enforce the critical rules a second time in the database, so they hold for scripts and tools as well as the app;
- stay readable for the people who run reports in SSMS.

The domain identifies everything by a business code (`BranchCode`, `ProductId`, `StaffId`, `TransactionCode`, …). These codes are immutable: no use case renames them.

## Decision

### Keys

- **Primary keys are surrogate `IDENTITY` columns** (`BranchId`, `CounterTransactionId`, …).
  - They are mapped as EF Core shadow properties, so the domain never sees them.
  - `bigint` is used on the high-volume tables (`CounterTransactions`, `TransactionLines`, `Shipments`, `StockAdjustments`, `AuditEntries`), `int` elsewhere.
  - The clustered key is narrow and ever-increasing, so inserts append at the end of the table instead of splitting pages in the middle.
- **Business codes are alternate keys** (`UNIQUE` constraints named `AK_…`).
- **Foreign keys reference the alternate keys.** For example, `BranchStock.BranchCode` references `Branches.BranchCode`.
  - A row is readable without joins, and the T-SQL in `/sql` and the procedures work with the codes people use.
  - The cost is wider foreign-key columns (`varchar(48)` instead of `int`). It is acceptable because codes never change.
- **Composite keys are used where the business key is composite.**
  - A counter is `(BranchCode, CounterCode)`.
  - `CounterTransactions` references it with one composite FK, which also proves the branch exists, so no second FK to `Branches` is needed.
  - A stock row is `(BranchCode, ProductCode)`, and `StockAdjustments` references that pair.
- **Other unique rules:** `Products.Sku` (`UX_Products_Sku`), `Staff.Email` (filtered `UX_Staff_Email … WHERE Email IS NOT NULL`, because email is optional), `Roles.Name`, and the shipping-rate band.

### Types

| Data | Type | Why |
|---|---|---|
| Money | `decimal(18,2)` | Exact arithmetic; matches `Money`'s two decimal places. |
| Points in time | `datetime2(3)`, UTC | `DateTimeOffset` values are converted to UTC. One time zone in storage makes "the day" unambiguous for reports. |
| Codes | `varchar(n)` | ASCII codes; half the size of `nvarchar`. Parameters are typed `varchar` too, so there are no implicit conversions (see `PERFORMANCE.md`). |
| Names and free text | `nvarchar(n)` | Branch, product and customer names can contain any character. |
| Enums (status, service level, outcome) | `varchar` holding the name, with a `CHECK … IN (…)` | Readable in reports, and reordering the enum cannot silently change stored meaning. |
| Staff role | `tinyint` FK to the `Roles` lookup table (seeded by migration) | The brief requires a `Roles` table; the FK keeps the allowed roles in data. |

### Constraints

**CHECK constraints** (26 in total) mirror domain rules:
- `Quantity >= 0` on stock;
- quantity and unit price on lines;
- `Amount > 0` and `Fee >= 0` on money orders;
- allowed status, service-level, shipment-type, payment-method and outcome values;
- a voided or refunded sale must name the authorising manager and a reason, and an open or paid sale must not;
- weights `> 0 AND <= 30` (the domain's `ParcelWeight.MaximumKilograms`);
- the masked identity reference must start with `****`;
- audit route values must be JSON.

**Audit columns:**
- Every table has `CreatedAtUtc`, `CreatedBy` and `ModifiedAtUtc`.
- `DEFAULT (SYSUTCDATETIME())` and `DEFAULT 'system'` cover rows written outside the app.
- `AuditColumnsInterceptor` stamps them from the signed-in staff member for rows written through EF Core.

**Concurrency and masking:**
- `rowversion` on `BranchStock` and `CounterTransactions` (ADR 0017).
- The masked identity reference is `varchar(8)`, so a full document number cannot fit, and a `CHECK` rejects any value that does not start with the mask.

### Delete behaviours

Nothing in the application deletes rows. Branches, counters and staff are closed or deactivated, and sales are voided or refunded. The application's database role has no `DELETE` permission (ADR 0012). The delete behaviour therefore decides what happens when a DBA deletes a row:

| Relationship | ON DELETE | Why |
|---|---|---|
| Lines and payment → counter transaction | `CASCADE` | They are owned parts of the sale and meaningless without it. Removing a sale removes its parts, never leaving orphans. |
| Identity verification → money order | `CASCADE` | Part of the money-order aggregate. |
| Everything else: counters/staff/stock/shipments/money orders → branch; stock/lines → product; sales → counter and staff; rates/shipments → zone; products → category; staff → role; adjustments → stock and staff | `NO ACTION` (restrict) | These rows are financial or operational history. Deleting a branch, product or staff member that has history must fail, not silently erase sales. |
| `Shipments.TransactionCode`, `MoneyOrders.TransactionContextCode` | No FK | The clerk reserves the transaction ID before the sale exists, and a refused money order never gets a sale. |
| `AuditEntries` | No FK | It must record failed sign-ins for unknown staff IDs and outlive later changes to the rows it mentions (ADR 0011). |

### Normalisation

The schema is in 3NF:
- reference data (roles, categories, zones, rates) lives in its own tables;
- a sale's lines and payment are separate tables keyed by the sale;
- stock is per `(branch, product)`.

Two deliberate copies are not redundancy:

- `TransactionLines.UnitPrice` and `Description` are the price and text **at the time of sale**: history must not change when the catalogue does.
- `CounterTransactions.Subtotal` and `Tax` are the amounts charged, derivable from the lines but fixed at payment. They are what the receipt showed.

`DESIGN.md` lists three anomalies this design prevents.

### Migrations and seed data

- **Schema:** EF Core code-first migrations (`InitialCreate`, then follow-ups). Every migration is reviewed before it is applied, and `database/ApexPostDb.idempotent.sql` is regenerated with `dotnet ef migrations script --idempotent`.
- **No `EnsureCreated`:** the application never calls `EnsureCreated()` or migrates at startup. The schema is applied by `dotnet ef database update` or by the script.
- **Reference data:** the roles and the `GENERAL` category are seeded by the migration (`HasData`), because the schema needs them.
- **Demo data:** the Development demo data comes from a seeding service that only fills an empty, fully migrated database.

## Consequences

- **The database enforces the rules itself.** Every critical rule fails in the database even when a script or a DBA bypasses the application.
- **History cannot be deleted by accident.** Removing a branch, product or staff member with history needs a deliberate decision, and the database refuses the shortcut.
- **Shadow keys need explicit mapping.** Domain entities stay free of database keys. In exchange, the EF configuration has to map shadow keys, alternate keys and composite foreign keys explicitly (`ConfigurationExtensions.HasSurrogateKey`).
- **Storage:** foreign keys and their indexes on `varchar` codes are wider than `int` keys would be. `PERFORMANCE.md` measures the indexes that matter.
