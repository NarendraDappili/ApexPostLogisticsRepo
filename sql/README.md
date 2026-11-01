# SQL: reporting queries, transactions and integrity

Hand-written T-SQL for Week 3, Part B. The application reaches the database through EF Core and the three stored procedures in `src/ApexPost.Infrastructure/Persistence/StoredProcedures/`. The scripts in this folder are for people who work in SSMS or `sqlcmd`: reporting, operations and teaching.

| File | Purpose | Writes data? |
|---|---|---|
| `01_daily_branch_sales_retail_vs_services.sql` | Paid sales per branch and day, retail vs services | No |
| `02_branches_with_no_sales_today.sql` | Open branches with no paid sale today (LEFT JOIN + HAVING) | No |
| `03_top_products_by_revenue_per_region.sql` | Top 10 catalogue products by revenue in each region | No |
| `04_shipping_volume_by_zone_and_service_level.sql` | Shipments per zone × service level, zeros included | No |
| `05_money_orders_and_verifications_per_branch.sql` | Money orders and identity checks per branch, plus the auditor exception list | No |
| `10_atomic_counter_sale.sql` | One sale: stock decrement, header, lines and payment in one transaction | **Yes** |
| `20_blocking_session1_writer.sql`, `21_blocking_session2_reader.sql`, `22_blocking_monitor.sql` | Blocking demo with two sessions and a monitor | Rolled back |

## Running the scripts

- In SSMS, open the file, select the database and execute it.
- From the command line, pass `-I` (QUOTED_IDENTIFIER ON), which `sqlcmd` leaves off by default. Without it, writes to tables with filtered indexes fail.

  ```
  sqlcmd -S . -E -d ApexPostDb -I -i sql\01_daily_branch_sales_retail_vs_services.sql
  ```
- Each report declares its inputs (dates, thresholds) at the top as variables. Values reach the query as variables, never by building SQL text.
- Run `10_atomic_counter_sale.sql` and the blocking demo against a **copy** of the database, never production. A copy of the local development database:

  ```sql
  BACKUP DATABASE ApexPostDb TO DISK = N'<backup folder>\ApexPostDb_copy.bak' WITH COPY_ONLY, INIT;
  RESTORE DATABASE ApexPostDb_Lab FROM DISK = N'<backup folder>\ApexPostDb_copy.bak'
      WITH MOVE N'ApexPostDb' TO N'<data folder>\ApexPostDb_Lab.mdf', MOVE N'ApexPostDb_log' TO N'<data folder>\ApexPostDb_Lab_log.ldf';
  ```

## Reporting queries

Rules shared by all five reports:

- **Which sales count:** a sale counts while its status is `Paid`, the same rule as `usp_DailyBranchSales` and the dashboard. Voided and refunded sales are left out.
- **Prices:** line amounts use the price stored on the line, which is the price at the time of sale.
- **Dates:** every date filter is a half-open UTC range (`>= @FromUtc AND < @ToUtc`) with no function on the column, so it stays SARGable.
- **Joins:**
  - Filters on the optional side of a LEFT JOIN go in the `ON` clause. In the `WHERE` clause they would remove the NULL rows and turn the LEFT JOIN into an INNER JOIN.
  - Totals are summed per parent row first wherever a join could repeat a value. For example, a sale's tax would otherwise be counted once per line.

### Verified against the demo data

The reports were run against the seeded development database: 242 sales from 1 September to 30 October 2026, two of them live smoke-test sales. Each figure was checked against an independent simpler query:

| Report | Result | Cross-check |
|---|---|---|
| 01 | BR-NORTH on 29 Oct 2026: retail 37.50, services 305.50, 1 paid sale | Same figures as `usp_DailyBranchSales` and the Reports page |
| 02 | 29 Oct: BR-WEST only. 30 Oct: BR-CENTRAL only. Today (1 Nov): all five branches | Matches the gaps in report 01. The seed rotates four sales a day over five branches. |
| 03 | 6 products per region; Central totals 2,416.25 | Same total from a plain `SUM` per region. The seed sells 6 distinct products per region, so fewer than 10 rows appear. |
| 04 | 119 shipments across zones 01, 03, 05, 07 and 09; the even zones show 0 | 119 = all shipments minus those in voided sales. The seed sends shipments only to odd zones. |
| 05 | 66 money orders, 66,500.00, 36 high-value orders, 31 verified and 5 failed checks. Each branch has one exception, its refused 2,000.00 order. | Same counts from a plain `COUNT`/`SUM` on `MoneyOrders` and `IdentityVerifications` |

## Atomic sale (`10_atomic_counter_sale.sql`)

The script records one counter sale exactly as the application's checkout does:
- stock decrement
- `CounterTransactions` header
- `TransactionLines`
- `Payments`

**How it is made atomic**

- **`SET XACT_ABORT ON`:** any run-time error dooms the transaction, including a constraint violation, a `THROW` or being chosen as a deadlock victim. The sale can never be half committed.
- **`BEGIN TRY` / `BEGIN CATCH` with `XACT_STATE()`:** the CATCH block reads `XACT_STATE()`:
  - `-1` means doomed; only a rollback is possible.
  - `1` means still committable.
  - `0` means no transaction is left.

  The sale is all-or-nothing, so the script rolls back whenever a transaction exists. It then re-raises the original error with `THROW`.
- **Validation and pricing before `BEGIN TRANSACTION`:** branch, counter, clerk, payment method and catalogue prices are all checked first, so the transaction holds locks only for its writes.
- **The stock check is inside the `UPDATE`** (`WHERE Quantity >= @Quantity`). Another session cannot sell the same units between the check and the write. `CK_BranchStock_Quantity` is a second guard.

**Verified on a copy of the database (`ApexPostDb_Lab`)**

| Run | Basket | Outcome |
|---|---|---|
| Commit | PRD-010 × 1, PRD-004 × 2 | Sale committed: subtotal 37.50, 2 lines, card payment 37.50. Stock went from 109 → 108 (PRD-010) and 103 → 101 (PRD-004). |
| Rollback | PRD-010 × 100000, PRD-004 × 2 | PRD-004 is processed first, because its stock row has the lower `BranchStockId`. A probe showed it at 99 *inside* the transaction. PRD-010 then failed. |

In the rollback run:
- The CATCH block saw `XACT_STATE() = -1`, rolled back and re-raised `Msg 50105 Not enough stock for a basket line`.
- Afterwards PRD-004 was back at 101 and PRD-010 still at 108.
- Sales, lines and payments still numbered 243 / 426 / 243, as before the run.

## Blocking demo

**Steps**

- **Session 1** (`20_…`) opens a transaction, updates the BR-NORTH stock of PRD-001 and keeps the transaction open.
- **Session 2** (`21_…`) then:
  - A: reads the row normally;
  - B: reads it with a locking read;
  - C: tries to update it.
- **The monitor** (`22_…`) shows the wait in `sys.dm_exec_requests` and the locks in `sys.dm_tran_locks`.

**Isolation level**

- Both sessions use the default isolation level, **READ COMMITTED** (`transaction_isolation_level = 2`).
- The database has **`READ_COMMITTED_SNAPSHOT` ON**:
  - EF Core turns it on when `dotnet ef database update` creates the database (it issues `ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON`).
  - A database created by other means has the SQL Server default, OFF. Turn it on there to get the same behaviour as the app expects.

  With the option ON, READ COMMITTED readers read the last committed row version and take no shared locks.

**Observed**

On `ApexPostDb_Lab`, run through `sqlcmd`: three concurrent sessions, with session 1 holding its transaction for 12 seconds.

| Step (session 2) | What happened | Waited | Value returned |
|---|---|---|---|
| Session 1 `UPDATE` (uncommitted) | X lock on the `PK_BranchStock` key | – | 3 (uncommitted) |
| A: plain `SELECT` | Not blocked: read the last committed version | 0 ms | **4** (committed) |
| B: `SELECT … WITH (READCOMMITTEDLOCK)` | Blocked: `LCK_M_S`, `blocking_session_id` = session 1 | 9,015 ms | 4, the moment session 1 rolled back |
| C: second `UPDATE` of the same row | Blocked: `LCK_M_U`, `blocking_session_id` = session 1 | 9,048 ms | Proceeded after the rollback, then rolled back itself |

What the monitor captured while session 2 was waiting:

- **Session 1:**
  - `X GRANT` on the `PK_BranchStock` key (the row);
  - `IX` on the page and table.
- **Session 2, step B:**
  - `S GRANT` on the `AK_BranchStock_BranchCode_ProductCode` key: the seek on the business key succeeded, because session 1 did not change that index;
  - `S WAIT` on the same `PK_BranchStock` key session 1 holds. That key is the lookup to read `Quantity`, which lives in the clustered index.
- **Session 2, step C:** the same pattern with update locks (`U GRANT` on the alternate key, `U WAIT` on the clustered key).

**Explanation**

- **What a writer blocks:** under RCSI a writer blocks other writers and locking readers, but not ordinary readers. If `READ_COMMITTED_SNAPSHOT` were OFF, step A would have waited just like step B. This is the classic READ COMMITTED behaviour: a shared lock is incompatible with an exclusive lock.
- **The app:**
  - EF Core queries are plain READ COMMITTED reads, so reports and screens never wait for an open sale, and a sale never waits for a report.
  - The trade-off: a reader can see a value that is about to change. The app never decides a write from such a read alone:
    - Stock and sale rows carry a `rowversion`, so a stale copy fails its `UPDATE … WHERE RowVersion = @original` with a concurrency conflict (409).
    - `usp_AdjustStock` checks stock inside its `UPDATE`.
- **Cost:** row versions live in tempdb, and updated rows carry a 14-byte version tag.

## Avoiding deadlocks

A deadlock needs two transactions that each hold a lock the other wants. The design prevents that cycle in three ways.

### 1. One table and row order for every writer

Every transaction that changes stock locks the `BranchStock` rows first, one row at a time in clustered-key (`BranchStockId`) order, and only then writes other tables.

| Writer | Order |
|---|---|
| Checkout (EF Core, one `SaveChanges`) | `BranchStock` rows by key → `CounterTransactions` → `TransactionLines` / `Payments` |
| Void / refund (EF Core, one `SaveChanges`) | `BranchStock` rows by key → `CounterTransactions` |
| `usp_AdjustStock` | one `BranchStock` row → `StockAdjustments` |
| First delivery (`StockAdjustmentService`) | new `BranchStock` row → `usp_AdjustStock` (the same row) → `StockAdjustments` |
| `10_atomic_counter_sale.sql` | `BranchStock` rows by `BranchStockId` → `CounterTransactions` → `TransactionLines` → `Payments` |

- **How the order is guaranteed:** the basket's line order is irrelevant. EF Core sorts the commands of one `SaveChanges`, and the integration test `Sales_and_voids_update_their_stock_rows_in_key_order_before_they_write_the_sale_rows` pins that order. It lists the basket in reverse key order and asserts that the stock `UPDATE`s still arrive in key order, before the sale rows.
- **Two sales of overlapping products:** they meet at the first shared stock row. The second waits there, holding no other stock row the first one needs.
- **Writes outside stock touch one table, in one short statement each:**
  - a price revision (`Products`);
  - an identity verification (`MoneyOrders`, `IdentityVerifications`);
  - staff and branch administration.

  Foreign-key checks take only brief shared locks on reference rows. Those rows are changed only by single-row administrative updates.

### 2. Short transactions

- **Nothing that waits runs inside a transaction:** reads, validation, pricing and permission checks happen before it starts.
- **EF Core opens the transaction only inside `SaveChanges`.**
- **`ExecuteInTransactionAsync`** (first delivery) wraps exactly one insert and one procedure call.
- **The audit trail** is written by its own context outside the business transaction ([ADR 0011](../docs/adr/0011-audit-store-outside-the-unit-of-work.md)). Low-stock alerts are raised after the commit.
- **Optimistic concurrency** (`rowversion`) replaces holding locks across a read-modify-write, so no lock outlives a request.

### 3. Readers do not take shared locks

With `READ_COMMITTED_SNAPSHOT` ON, reports and screens read row versions. The most common deadlock between a reporting query and an update cannot form.

### If one happens anyway

- `SET XACT_ABORT ON` in the procedures and scripts rolls the victim back cleanly.
- In the app, the SQL Server execution strategy (`EnableRetryOnFailure`) treats error 1205 (deadlock victim) as transient and replays the whole unit of work.
