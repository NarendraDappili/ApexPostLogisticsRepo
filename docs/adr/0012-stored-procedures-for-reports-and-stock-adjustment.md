# ADR 0012: Use stored procedures for three reports and for stock adjustment

Status: Accepted.

## Context

Most data access goes through EF Core: repositories plus the unit of work for writes, and LINQ projections for reads. Four operations have needs that LINQ does not meet well. The fourth, sales for a period, was added on 2 November 2026 when the reports page gained a from-to date range. Speed alone is not the reason: a parameterised LINQ query is also compiled once and cached by SQL Server.

## Decision

### `usp_DailyBranchSales (@BranchCode, @Date)`

- **What it does:** totals a branch's paid sales for one UTC day, split into retail (catalogue lines) and services (shipment and money-order lines), with tax and the paid-sale count.
- **Why a procedure:**
  - Line prices live in an *owned* type mapped through a value converter. EF can neither `SUM` a converted value in SQL nor map that owned property as a complex type. The LINQ version had to bring the day's lines back and add them up in the application; the procedure does the whole aggregation in one statement.
  - Head office wants the same figures from SSMS and reporting tools. A procedure is one reviewed contract that those tools can run with `EXECUTE` permission and no read access to the tables.
- **SARGable date filter:** the date becomes a half-open `datetime2` range, so the filter on `OccurredAtUtc` is index-friendly.

### `usp_BranchSalesForPeriod (@BranchCode, @FromDate, @ToDate)`

- **What it does:** the same totals as `usp_DailyBranchSales`, one row per UTC day from `@FromDate` to `@ToDate` (both included), for days with a paid sale. The reports page adds the rows up for the period's totals and lists them as "Sales by day".
- **Why a procedure:** the same reasons as the daily report: the owned, converted line prices cannot be summed in LINQ, and head office can run it from SSMS.
- **Why a new procedure rather than a changed one:** `usp_DailyBranchSales` is the brief's daily report, with measured evidence behind it (PERFORMANCE.md, Investigation 1). It stays unchanged and still serves a one-day request. A period runs the new procedure.
- **`OPTION (RECOMPILE)`:** a period can be a week at a small branch or a year at the flagship, about 3,000 times more rows. A plan cached for a short period and reused for a year read 250,271 pages; recompiled, it read 4,840 (PERFORMANCE.md, Investigation 4).
- **Limits:** the start must be on or before the end (`THROW 50044`), and a period is at most 366 days (`THROW 50045`). The application checks the same rules first (`BranchSalesPeriodReport.Validate`), so a user sees a message rather than a database error.

### `usp_LowStockReport (@BranchCode = NULL, @Threshold)`

- **What it does:** lists products at or below the threshold for one branch, or for every branch when `@BranchCode` is NULL (the head-office view the app cannot express with a single LINQ query).
- **Why a procedure:** the optional parameter creates a catch-all predicate, `(@BranchCode IS NULL OR BranchCode = @BranchCode)`. A single cached plan is wrong for one of the two cases: a seek for one branch, a scan for all.
- **`OPTION (RECOMPILE)`:** the plan is compiled for the actual values each time, and SQL Server removes the branch of the predicate that does not apply. The report runs a few times a day, so the compile cost is negligible.

  Alternatives considered:
  - `IF @BranchCode IS NULL … ELSE …` with two statements: duplicates the query and still sniffs `@Threshold`.
  - Dynamic SQL through `sp_executesql`: two cached plans, but more code and string building.
  - `OPTIMIZE FOR UNKNOWN`: one average plan that is mediocre for both cases.

### `usp_AdjustStock (@BranchCode, @Sku, @Delta, @Reason, @ChangedBy)`

- **What it does:** changes one branch stock row and writes the matching `StockAdjustments` history row, both or neither. It returns the new quantity.
- **Why a procedure:**
  - The rules "only an active Branch Manager of the branch adjusts its stock", "every manual correction has a history row" and "stock never goes below zero" are enforced **in the database**. They therefore also hold for corrections that do not come through the web app, such as an operator script or a DBA fix. The manager check (`THROW 50025`) joins `Staff` to `Roles` and compares the staff member's branch with `@BranchCode`, the same rule `StockAdjustmentService` applies; `StoredProcedureTests` shows a clerk, a manager of another branch and an inactive manager are all refused.
  - The quantity check is part of the `UPDATE` (`WHERE Quantity + @Delta >= 0`), so no concurrent change can slip between the check and the write.
  - The rowversion still changes, so a clerk's stale EF copy of that row is rejected at commit.
- **Transaction handling:**
  - `SET XACT_ABORT ON` dooms the transaction on any run-time error.
  - It starts and commits a transaction only when the caller has none open.
  - `TRY…CATCH` rolls back what it started and re-raises with `THROW`.
- **The mixed transaction in the app:** a first delivery inserts the stock row through the unit of work (`SaveChangesAsync`) and then calls the procedure. Both run inside `IUnitOfWork.ExecuteInTransactionAsync`, which opens one explicit transaction inside the execution strategy. A failure in the procedure rolls back the earlier insert.

### Shared rules

- **Deployment:** each migration that deploys a procedure keeps the exact `CREATE OR ALTER` text it shipped, as a constant in the migration class, and runs it through `EXEC(N'…')`. `CREATE PROCEDURE` must start a batch, and idempotent scripts wrap each migration in an `IF` block. Migrations never read the `.sql` files, so replaying one on a new database deploys the same body it deployed originally.
- **Current version:** the `.sql` files in `src/ApexPost.Infrastructure/Persistence/StoredProcedures/` hold the current version of each procedure, for review and SSMS. `StoredProcedureTests` checks that a fully migrated database runs exactly those files, and `StoredProcedureMigrationTests` checks that the latest migration for each procedure carries that text.
- **Validation:** each procedure starts with `SET NOCOUNT ON`, validates its parameters and raises errors with `THROW 500xx`.
- **Calls from EF Core:** always parameterised (`SqlQuery<T>($"EXEC … @Name = {value}")` or `ExecuteSqlAsync`). The interpolated values become `DbParameter`s, never SQL text.
- **Errors in the app:** `THROW` errors (50000–59999) become `RuleViolationException` and are shown as a business-rule message or a 400 Problem Details.

### Least privilege

`database/security/apexpost-app-role.sql` creates the `apexpost_app` role for the application's database user.

| Allowed | Not allowed |
|---|---|
| `SELECT`/`INSERT`/`UPDATE` only on the tables the use cases write | `DELETE` on any table |
| `SELECT` only on reference data | Any DDL |
| `SELECT, INSERT` on `AuditEntries` (append-only for the app) | `UPDATE` or `DELETE` on `AuditEntries` |
| `EXECUTE` on the four procedures | Any direct permission on `StockAdjustments` |

- History rows are written only by `usp_AdjustStock`, which reaches `StockAdjustments` through ownership chaining.
- Migrations and the Development seed run under a separate deployment login.

Verified on a throwaway database with a member of the role:
- **Allowed:** reads, the price-revision `UPDATE`, inserting audit rows, and both procedures.
- **Refused with Msg 229 (no permission):** `UPDATE`/`DELETE` on `AuditEntries`, `DELETE` on `CounterTransactions`, and any direct access to `StockAdjustments`.
- **Refused with Msg 262:** `CREATE TABLE`.
- **Rechecked on 2 November 2026** for `usp_BranchSalesForPeriod`. A member of the role ran it, and `DELETE` on `CounterTransactions` was still refused (Msg 229).

## Consequences

- Report figures and stock corrections behave the same from the app, SSMS and scripts.
- Procedure logic lives in T-SQL, so it is tested against a real database (`StoredProcedureTests`) rather than with fakes.
- Changing a procedure means changing its script and adding a migration that carries the new body. `RedeployDailyBranchSales` (a cardinality misestimate found in `PERFORMANCE.md`) and `AdjustStockRequiresBranchManager` follow the pattern:
  - `Up` deploys the body kept in the new migration.
  - `Down` deploys the body kept in the migration before it.
  - A migration's text never changes after it ships. A fresh database therefore passes through each version in order, exactly as an existing one did, and `database/ApexPostDb.idempotent.sql` shows each version in the migration that deployed it.
- Adding a procedure follows the same pattern. `AddBranchSalesForPeriod` keeps the body it shipped, its `Down` drops the procedure, and a test checks both directions.
- Shipping volume and the money-order summary stay as LINQ projections; they have none of the needs above.
