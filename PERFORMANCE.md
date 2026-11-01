# Performance: indexes and execution plans

This document records how the workload indexes were chosen and what they changed. The numbers were measured on SQL Server 2025 against a one-year data set of 200,000 counter transactions. Every number comes from a saved file in [`docs/performance/`](docs/performance/): result CSVs, `.sqlplan` files and raw text captures. Each one can be reproduced with the scripts in [`database/perf/`](database/perf/).

## Summary

| Workload (branch) | Logical reads before → after | CPU ms before → after | Plan before → after |
|---|---|---|---|
| W1 low-stock report (one branch) | 2,158 → **24** | 62 → 0–15 | Clustered scan of `BranchStock` → seek on `IX_BranchStock_BranchCode_Quantity` |
| W1 low-stock report (all branches) | 2,156 → **919** | 63 → 47 | Clustered scan (2,137 pages) → scan of the narrower new index (900 pages) |
| W2 daily sales (flagship BR-0001) | 20,885 → **371** | 267 → 0 | Parallel (DOP 4) clustered scans → two covering seeks |
| W2 daily sales (typical BR-0100) | 7,456 → **14** | 0 → 0 | FK-index seek + 804 key lookups → covering seeks |
| W3 shipping volume (BR-0001) | 1,765 → **113** | 47 → 0 | Clustered scan → covering seek |
| W3 shipping volume (BR-0100) | 1,241 → **6** | 0 → 0 | FK-index seek + 403 key lookups → covering seek |
| W4 money-order report (BR-0001) | 150 → **69** | 15 → 16 | Clustered scan of `IdentityVerifications` → scan of the covering index |
| W4 money-order report (BR-0100) | 121 → **36** | 0 → 0 | Same as BR-0001 |
| W5 branch dashboard (BR-0001) | 6,126 → **20** | 124 → 0 | Parallel scans of `BranchStock` and `CounterTransactions` → seeks |
| W5 branch dashboard (BR-0100) | 4,620 → **18** | 47 → 0 | Scan of `BranchStock` + lookups → seeks |

The cost of these gains:

- **Storage:** about **23.1 MB** of index on a 279.7 MB database (+9%).
- **Writes:** in steady state, about **30% more transaction log** for the same mixed write batch. Most of it comes from moving a row in `IX_BranchStock_BranchCode_Quantity` on every stock change.

Two further findings:

- **A real estimated-vs-actual mismatch in `usp_DailyBranchSales`.** It came from local variables, and the new index alone did not fix it. The procedure was changed and redeployed by a migration.
- **The non-SARGable rewrite** (`YEAR()`/`MONTH()` → date range) reads **27 pages instead of 282** on the same index.

## Environment and method

**Environment**

| | |
|---|---|
| Server | SQL Server 2025 (RTM) 17.0.1000.7, Enterprise Evaluation, local default instance on Windows (hypervisor) |
| Database | `ApexPostDb_Perf`, created by `dotnet ef database update` from the same migrations as every other environment |
| Settings | Compatibility level 170, `READ_COMMITTED_SNAPSHOT ON` (EF Core sets it when it creates a database), Parameter Sensitive Plan optimization ON (the default at level 170) |
| Statistics | `UPDATE STATISTICS … WITH FULLSCAN` on every table after the load. Index creation builds full-scan statistics for each new index. Later automatic updates, triggered by the rolled-back write batches, re-sampled some other statistics; each `.sqlplan` lists the statistics it used and when they were updated (`OptimizerStatsUsage`). |

**Method** (implemented by [`Measure-Workloads.ps1`](database/perf/Measure-Workloads.ps1))

- **Same SQL as the app:** each workload is sent exactly as the application sends it, with the same parameter types:
  - EF Core queries as parameterised `sp_executesql` batches (the SQL text is the `ToQueryString()` output below);
  - procedures as parameterised `EXEC`.
- **Fresh plan per workload:** `ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE` runs before each workload, so each one gets its own compiled plan. Plan reuse across branches is investigated separately below.
- **Runs:** one warm-up run, then five measured runs.
  - All numbers are warm-cache numbers. Logical reads come from `SET STATISTICS IO` and were identical on every run.
  - CPU and elapsed time come from the last `SET STATISTICS TIME` message, which covers the whole call, and are reported as medians.
- **Actual plan:** one further run with `SET STATISTICS XML ON` is saved as a `.sqlplan` file. Open it in SSMS.
- **CPU timer resolution:** CPU time on Windows advances in steps of about 15.6 ms, which is why the individual runs show 0, 15, 16, 31, 47, 62. A median of 0–16 ms means "under one timer step". For the small queries, logical reads and plan shape are the meaningful measures; CPU is not.

## Data set

The demo database has 242 transactions, which is too small for meaningful plans (see "Small data" below). [`01-load-perf-data.sql`](database/perf/01-load-perf-data.sql) is a set-based, deterministic load: no `RAND()` or `NEWID()`. It refuses to run anywhere except an empty `ApexPostDb_Perf`.

| Table | Rows |
|---|---|
| Branches / Counters / Staff | 200 / 600 / 800 |
| Products / BranchStock | 1,000 / 200,000 (every branch stocks every product) |
| CounterTransactions / Payments | 200,000 / 200,000 (1 Nov 2025 – 30 Oct 2026, in time order) |
| TransactionLines | 528,573 |
| Shipments | 100,000 |
| MoneyOrders / IdentityVerifications | 29,572 / 10,524 |

The distribution is skewed on purpose, like a real network:

- **Flagship branch:** BR-0001 has 20% of all transactions (40,000; 20,000 shipments; 6,715 money orders). The other 199 branches have 804–805 transactions each.
- **Statuses:** 96% of transactions are Paid, 2% Refunded and 2% Voided. Because of the generation rule, all of BR-0001's transactions are Paid. Status is a residual predicate in every plan below, so this does not affect any plan choice.
- **Low stock:** 2% of stock rows are at or below 10 units, which is 20 per branch.

## Workloads

| | Workload | Issued by | SQL |
|---|---|---|---|
| W1 | Low-stock report by branch (and all branches) | Reports page, `usp_LowStockReport` | `BranchStock ⋈ Products WHERE BranchCode = @b AND Quantity <= @t` |
| W2 | Daily sales by branch and date | Reports page, `usp_DailyBranchSales` | Paid sales of the day, joined to their lines |
| W3 | Shipments by zone and service level for a branch | Reports page, `EfBranchReportQueries.ShippingVolumeQuery` | `GROUP BY ZoneCode, ServiceLevel` |
| W4 | Money-order report for auditors (verification outcomes) | Reports page, `EfBranchReportQueries.MoneyOrderOutcomesQuery` | `MoneyOrders LEFT JOIN IdentityVerifications GROUP BY Outcome` |
| W5 | Branch dashboard (one statement, three counts) | Dashboard, `EfBranchQueries.DashboardQuery` | See the `ToQueryString` sample below |
| S1 | One month of a branch's paid sales | Non-SARGable experiment | `YEAR()`/`MONTH()` vs a date range |

Each workload ran for the flagship (BR-0001) and for a typical branch (BR-0100), on 15 October 2026 where a date is needed.

## Baseline

Measured before the index migration, with only the keys, unique constraints and foreign-key indexes from `InitialCreate`. Data: [`results-baseline.csv`](docs/performance/results-baseline.csv). Plans: [`plans/baseline/`](docs/performance/plans/baseline/).

| Workload | Reads (by table) | CPU / elapsed ms | Plan shape (estimated / actual rows at the access operator) |
|---|---|---|---|
| W1 BR-0001 | 2,158 (BranchStock 2,137) | 62 / 59 | Clustered Index Scan `PK_BranchStock` (est 123.8 / act 20) |
| W1 BR-0100 | 2,158 (BranchStock 2,137) | 62 / 58 | Same |
| W1 all branches | 2,156 | 63 / 84 | Clustered Index Scan `PK_BranchStock` (est 3,066 / act 4,000) |
| W2 BR-0001 | 20,885 (CounterTransactions 11,577, TransactionLines 9,306) | 267 / 87 | **DOP 4**; three clustered scans of `CounterTransactions` (est **16,131** / act **110** each), a clustered scan of `TransactionLines`; 5,912 KB memory grant |
| W2 BR-0100 | 7,456 (CounterTransactions 7,440) | 0 / 9 | Seek on FK index `IX_CounterTransactions_BranchCode_CounterCode` (804 rows) + key lookups, three times |
| W3 BR-0001 | 1,765 | 47 / 47 | Clustered Index Scan `PK_Shipments` (20,000 rows) |
| W3 BR-0100 | 1,241 | 0 / 1 | Seek on FK index `IX_Shipments_OriginBranchCode` + 403 key lookups |
| W4 BR-0001 | 150 (IdentityVerifications 118) | 15 / 11 | Clustered scan of `IdentityVerifications` (10,524 rows), hash join |
| W4 BR-0100 | 121 | 0 / 5 | Same |
| W5 BR-0001 | 6,126 (BranchStock 2,244, CounterTransactions 3,859) | 124 / 44 | DOP 4; clustered scans of `BranchStock` and `CounterTransactions` |
| W5 BR-0100 | 4,620 (BranchStock 2,137, CounterTransactions 2,471) | 47 / 54 | `BranchStock` scan; FK-index seek + lookups on `CounterTransactions` |

**What the baseline shows**

- **`BranchStock` has no index containing `Quantity`.** The business key `(BranchCode, ProductCode)` can find a branch's 1,000 rows, but each would need a key lookup to read `Quantity`. The optimizer correctly prefers scanning all 200,000 rows (2,137 pages).
- **The dashboard pays for that scan on every page view.**
- **The single-column FK indexes find the rows but don't cover the query.** A typical branch pays one lookup per row. For the flagship, the optimizer gives up on the index and scans.
- **The plan changes with the branch.** For the same query the flagship gets a scan and a typical branch gets a seek with lookups. That is the condition for parameter sensitivity, investigated below.

## Index design

All five indexes are declared once, with the Fluent API in the `IEntityTypeConfiguration` classes, and created by the follow-up migration [`AddWorkloadIndexes`](src/ApexPost.Infrastructure/Persistence/Migrations/20261101123330_AddWorkloadIndexes.cs).
- **Not the `[Index]` attribute:** it would have to sit on the Domain entities, and it cannot express INCLUDE columns.
- **No raw-SQL copies.**
- **Pinned by a test:** `ApexPostModelTests.Workload_indexes_have_the_measured_key_order_and_included_columns` fixes each key order and INCLUDE list.

| Index | Type | Key (order) | INCLUDE |
|---|---|---|---|
| `IX_BranchStock_BranchCode_Quantity` | New, composite + covering | `BranchCode`, `Quantity` | `ProductCode` |
| `IX_CounterTransactions_BranchCode_OccurredAtUtc` | New, composite + covering | `BranchCode`, `OccurredAtUtc` | `Status`, `Subtotal`, `Tax` |
| `IX_TransactionLines_CounterTransactionId` | Existing FK index, made covering | `CounterTransactionId` | `ProductCode`, `Quantity`, `UnitPrice` |
| `IX_Shipments_OriginBranchCode_ZoneCode_ServiceLevel` | Composite, **replaces** the FK index `IX_Shipments_OriginBranchCode` | `OriginBranchCode`, `ZoneCode`, `ServiceLevel` | – |
| `IX_IdentityVerifications_MoneyOrderId` | Existing unique FK index, made covering | `MoneyOrderId` | `Outcome` |

### Why each key order and INCLUDE list

- **`BranchStock (BranchCode, Quantity) INCLUDE (ProductCode)`**
  - **Key order:** equality column first, range column second. One branch's low rows then form a single short range: 3 reads for 20 rows.
  - **Reversed order rejected:** `(Quantity, BranchCode)` would read every branch's low rows (4,000) to find one branch's 20. That is the common case: the dashboard on every page view.
  - **INCLUDE:** `ProductCode` is included because both the report and the join to `Products` need it. The clustered key (`BranchStockId`) is carried automatically.
  - **Filtered index rejected:** `WHERE Quantity <= 10` would be tiny, but the threshold is configuration (`InventoryServiceOptions.LowStockThreshold`). It reaches SQL Server as a parameter, and SQL Server can only match a filtered index when the predicate is known at compile time.
- **`CounterTransactions (BranchCode, OccurredAtUtc) INCLUDE (Status, Subtotal, Tax)`**
  - **Key order:** equality on the branch, then the half-open date range. Every sales query in the app has that shape: the daily sales procedure, the dashboard count, and the recent-transactions list (`ORDER BY OccurredAtUtc DESC`).
  - **The recent-transactions list was checked too:** it reads this index in a backward ordered seek (50 rows in 3 reads), then makes 50 lookups for the columns not included. That is 165 reads for BR-0001, with no Sort.
  - **`Status` is INCLUDEd, not keyed.** 96% of rows are Paid, so it hardly narrows anything. As a middle key column, it would split the recent-transactions list (any status) into one range per status plus a sort.
  - **Filtered `WHERE Status = 'Paid'` index rejected** for the same reason: it would save about 4% of rows but no longer serve that list.
  - **`Subtotal` and `Tax`** cover the totals that sales reports add up.
- **`TransactionLines (CounterTransactionId) INCLUDE (ProductCode, Quantity, UnitPrice)`**
  - The FK index already had the right key. Only the INCLUDE list was missing, so reading a day's lines no longer needs a lookup per line.
  - The `Description` column (nvarchar(250)) is deliberately not included.
- **`Shipments (OriginBranchCode, ZoneCode, ServiceLevel)`**
  - **Key order:** the equality column first, then the two `GROUP BY` columns in grouping order. The rows arrive already grouped, so a Stream Aggregate counts them with no sort, no hash and no lookups.
  - **Replaces the FK index:** the index starts with the FK column, so EF Core drops its own single-column FK index on `OriginBranchCode`. The migration replaces that index rather than adding one more.
- **`IdentityVerifications (MoneyOrderId) INCLUDE (Outcome)`**
  - The report groups by `Outcome`. Including it lets the join read this index (36 pages) instead of the clustered index (118 pages).

## After the indexes

There are two "after" runs:
- **`indexed`:** straight after `AddWorkloadIndexes` ([`results-indexed.csv`](docs/performance/results-indexed.csv), [`plans/indexed/`](docs/performance/plans/indexed/)).
- **`final`:** after the `usp_DailyBranchSales` fix explained below ([`results-final.csv`](docs/performance/results-final.csv), [`plans/final/`](docs/performance/plans/final/)).

They differ only for W2, because W2 is the only workload that uses that procedure.

| Workload | Reads | CPU / elapsed ms | Plan shape (final) |
|---|---|---|---|
| W1 BR-0001 | 24 (BranchStock **3**) | 0 / 8 | Index Seek `IX_BranchStock_BranchCode_Quantity` (20 rows), Sort, hash join to `Products` (19 pages) |
| W1 BR-0100 | 24 | 15 / 14 | Same |
| W1 all branches | 919 (BranchStock 900) | 47 / 49 | Index Scan of the new index: 900 pages instead of 2,137 |
| W2 BR-0001, `indexed` | **2,853** (TransactionLines 2,836) | 94 / 111 | Seek on the new index (est **16,131** / act **110**) **but a full scan of `IX_TransactionLines_CounterTransactionId` (507,588 rows)** |
| W2 BR-0001, `final` | **371** (TransactionLines 354) | 0 / 1 | Seek on the new index (est 274 / act 110), then 110 seeks into the covering lines index; no memory grant |
| W2 BR-0100, `final` | 14 | 0 / 0 | Covering seeks only |
| W3 BR-0001 | 113 | 0 / 8 | Index Seek on the composite (20,000 rows), Stream Aggregate |
| W3 BR-0100 | 6 | 0 / 0 | Same (403 rows) |
| W4 BR-0001 | 69 | 16 / 12 | Seek `IX_MoneyOrders_BranchCode` (6,715) + scan of the covering `IX_IdentityVerifications_MoneyOrderId` |
| W4 BR-0100 | 36 | 0 / 2 | Same |
| W5 BR-0001 | 20 | 0 / 0 | Seeks on both new indexes, serial plan |
| W5 BR-0100 | 18 | 0 / 0 | Same |

**Why some operators still scan**

- **W1 all branches:** "every product at or below 10 units in every branch" has no leading equality to seek on. The scan is now of a 900-page index instead of the 2,137-page table.
- **W1 `Products`:** a 1,000-row, 19-page table. A hash join over a scan of it is cheaper than 20 nested-loop seeks with lookups.
- **W4:** for 6,715 money orders, one pass over the 36-page covering index is cheaper than 6,715 seeks. For BR-0100's 116 orders the optimizer makes the same choice, and the whole query reads 36 pages.

## Non-SARGable predicate fixed (S1)

The same question, one month of a branch's paid sales, written two ways:

```sql
-- Non-SARGable: a function on the column
WHERE t.BranchCode = @branchCode AND t.Status = 'Paid'
  AND YEAR(t.OccurredAtUtc) = @year AND MONTH(t.OccurredAtUtc) = @month
-- SARGable: a half-open range on the bare column
WHERE t.BranchCode = @branchCode AND t.Status = 'Paid'
  AND t.OccurredAtUtc >= @from AND t.OccurredAtUtc < @to
```

| | Baseline (no date index) | With `IX_CounterTransactions_BranchCode_OccurredAtUtc` |
|---|---|---|
| `YEAR()`/`MONTH()` | Clustered scan; 200,000 rows read; 3,671 reads; 78 ms CPU | Seek on **`BranchCode` only**. `datepart(year…)` and `datepart(month…)` are evaluated as a residual predicate on all **40,000** branch rows: **282 reads**, 16 ms CPU |
| Date range | Clustered scan; 200,000 rows read; 3,671 reads; 63 ms CPU | Seek on **`BranchCode, OccurredAtUtc`**; reads exactly the month's **3,302** rows; **27 reads**, 0 ms CPU |

Plans: [`plans/baseline/S1-*`](docs/performance/plans/baseline/) and [`plans/final/S1-*`](docs/performance/plans/final/). Compare the `Seek Predicates` and `Predicate` properties of the index operator.

**What this shows**

- **The rewrite and the index are both needed.** Before the index existed, both forms scanned, because there was nothing to seek on. With the index, only the range form can use the second key column; the function form is limited to the branch prefix.
- **The app already uses the SARGable form.** Every date filter it sends uses half-open ranges: `UtcDay.Range` in the query services, the procedures, and every script in [`/sql`](sql/).
- **The parameter types are safe.** The dashboard's `datetime2` parameters are compared with a `datetime2(3)` column and still produce a seek on `OccurredAtUtc`. The only `CONVERT_IMPLICIT` in the final plans converts `COUNT(*)` from bigint to int and never touches a column.

## Estimated vs actual rows

### Investigation 1: a fixed guess in `usp_DailyBranchSales` (found, explained, fixed)

**Symptom**
- W2 for BR-0001 estimated **16,131 paid sales** for the day; there were **110**.
- **Baseline:** that estimate produced a parallel plan (DOP 4) with three clustered scans and a 5,912 KB memory grant, of which 1,808 KB was used.
- **After the index:** the sales seek was perfect (15 reads), but the misestimate still drove the plan. The optimizer expected about 16,000 sales, so it judged one pass over all 528,573 lines (2,836 reads) cheaper than 16,000 seeks, when 110 seeks were needed. Memory grant: 1,536 KB, of which 16 KB was used.

**Cause: local variables, not stale statistics or parameter sniffing**
- The procedure turned `@Date` into `DECLARE @From = CAST(@Date …)` and `@To = DATEADD(DAY, 1, @From)`, then filtered on `@From` and `@To`.
- A local variable's value is unknown when the procedure is compiled, so the optimizer uses a fixed guess for the range instead of the histogram.
- **Proof:** an estimated plan of the same predicate with local variables gives **403.215 rows** whatever the date: 15 Oct 2026, 1 Mar 2026, and 1 Jan 2031, a date with no rows at all.
- **Statistics were fresh:** full scan after the load, and built at index creation.

**Fixes tried on probe copies of the procedure**

| Option | Line reads for BR-0001 | Notes |
|---|---|---|
| Unchanged (local variables) | 2,836 | Full scan of the lines index |
| `OPTION (RECOMPILE)` on the statement | 378 | Variables embedded at compile time; pays a compile on every call |
| **Bounds as expressions of `@Date`** (no local variables) | **378** | The sniffed `@Date` drives the estimate; the plan is cached and reused. BR-0100 reusing the flagship's plan cost 15 reads in total. |
| `@From`/`@To` as procedure parameters | not tried | Changes the procedure contract, which the brief fixes as `(@BranchCode, @Date)` |

**Change shipped**
- The procedure now filters `t.OccurredAtUtc >= CAST(@Date AS datetime2(3)) AND t.OccurredAtUtc < DATEADD(DAY, 1, CAST(@Date AS datetime2(3)))`. The function is applied to the parameter, never to the column, so the predicate stays SARGable.
- It is deployed by the migration [`RedeployDailyBranchSales`](src/ApexPost.Infrastructure/Persistence/Migrations/20261101123522_RedeployDailyBranchSales.cs). Its `Down` restores the previous body; up and down were both verified.
- **Result:** estimate 274 against 110 actual (it was 16,131), **371 reads, no memory grant, serial plan**. The procedure still returns the same figures: BR-0001 on 15 Oct gives 11,558.25 / 11,796.00 / 110, and the dev database's BR-NORTH on 29 Oct gives 37.50 / 305.50 / 1.

### Investigation 2: parameter sensitivity of the shipping report (plan reuse)

EF Core sends the shipping report as one parameterised statement, so SQL Server compiles one plan and reuses it for every branch. [`02-parameter-sensitivity.sql`](database/perf/02-parameter-sensitivity.sql) compiles that plan for BR-0100 (403 shipments) and then runs it for BR-0001 (20,000).

**Before the indexes** ([`plans/baseline/E1-…`](docs/performance/plans/baseline/E1-shipping-volume-BR-0001-reusing-BR-0100-plan.sqlplan))

| Operator | Estimated | Actual |
|---|---|---|
| Index Seek `IX_Shipments_OriginBranchCode` | 403 rows | **20,000** rows |
| Key Lookup (Clustered Index Seek `PK_Shipments`) | 403 executions | **20,000** executions |
| Sort | 403 rows; 1,536 KB grant | 20,000 rows, **spilled to tempdb** (64 pages written and read back) |

- **Cost:** 61,350 logical reads, against 1,765 when BR-0001 gets its own plan (35 times more).
- **The plan carries the evidence.** It records `ParameterCompiledValue = 'BR-0100'` with `ParameterRuntimeValue = 'BR-0001'`, and its memory-grant feedback is marked "Adjusting".
- **SQL Server 2025's Parameter Sensitive Plan optimization did not step in** for this statement: the cache held one plan with `execution_count = 2` and no query variants.

**After the indexes** ([`plans/final/E1-…`](docs/performance/plans/final/E1-shipping-volume-BR-0001-reusing-BR-0100-plan.sqlplan))
- The same reuse costs **113 reads**: a single seek on the composite index, estimated 403 against 20,000 actual, then a Stream Aggregate.
- There is no lookup, no Sort and no memory grant, so the wrong estimate no longer costs anything.
- A covering index whose order matches the `GROUP BY` makes one plan right for every branch size, which removes the sensitivity rather than working around it with hints.

## Write overhead

[`04-write-overhead.sql`](database/perf/04-write-overhead.sql) replays a batch of the application's writes in one transaction and rolls it back, so the data set never changes:
- 2,000 stock movements, one `UPDATE` per row by primary key, as EF Core sends them;
- 1,000 sales with 2,000 lines and 1,000 payments;
- 500 shipments;
- 200 money orders with verifications.

It ran three times before and three times after the index migration, each series starting from freshly created indexes (`dotnet ef database update` down to `AddStoredProcedures`, then up again). Raw output: [`write-overhead-baseline.txt`](docs/performance/write-overhead-baseline.txt), [`write-overhead-final.txt`](docs/performance/write-overhead-final.txt).

| Run | CPU ms | Logical reads | Log records | Log bytes |
|---|---|---|---|---|
| Before, run 1 | 238 | 74,400 | 15,276 | 2,673,292 |
| Before, runs 2–3 | 154 / 162 | 73,240 / 73,202 | 15,314 | 2,619,064 / 2,619,272 |
| After, run 1 | 442 | 99,963 | 20,276 | **8,488,564** |
| After, runs 2–3 | 191 / 258 | 89,682 / 90,799 | **20,314** | **3,417,252 / 3,417,044** |

**Row operations per index** (from `sys.dm_db_index_operational_stats`)

| Index | Work for this batch | Per business operation |
|---|---|---|
| `IX_BranchStock_BranchCode_Quantity` | 2,000 deletes + 2,000 inserts | Every stock change moves the row, because `Quantity` is a key column |
| `IX_CounterTransactions_BranchCode_OccurredAtUtc` | 1,000 inserts | One per sale |
| The three widened or replaced indexes | Same row count as before (2,000 / 500 / 200 inserts), wider rows | No extra rows |

**Steady-state cost (runs 2–3)**
- **+5,000 log records.** That is exactly the 4,000 + 1,000 extra row operations above, a 33% increase.
- **+0.80 MB of log** (798,188 bytes, +30%) and about **+17,000 logical reads** (+23%) for the batch. CPU varies between runs; it is 29–104 ms higher.
- **Per checkout:** a typical sale with two catalogue lines costs **five extra index row operations**: two deletes and two inserts in the stock index, and one insert in the sales index.

**First run after creation: page splits**
- The first run after creating the indexes wrote **8.5 MB of log**. The index statistics show **863 new leaf pages** in `IX_BranchStock_BranchCode_Quantity` during that run, against 0 in the following runs.
- A new index is 100% full. A stock change moves the row to its new `Quantity` position inside a full page, which splits the page. After the first wave of splits, pages have free space and later changes fit.
- A `FILLFACTOR` below 100 on that index would avoid the first wave at the cost of more pages. It was not measured, so it is not applied; it remains an option if the log spike after a rebuild matters in production.

## Storage cost

Used pages per index, measured straight after index creation ([`storage-baseline.txt`](docs/performance/storage-baseline.txt), [`storage-final.txt`](docs/performance/storage-final.txt); 1 page = 8 KB):

| Index | Before | After | Change |
|---|---|---|---|
| `IX_BranchStock_BranchCode_Quantity` | – | 904 pages (7.06 MB) | +7.06 MB |
| `IX_CounterTransactions_BranchCode_OccurredAtUtc` | – | 1,405 pages (10.98 MB) | +10.98 MB |
| `IX_TransactionLines_CounterTransactionId` | 2,377 pages (18.57 MB) | 2,958 pages (23.11 MB) | +4.54 MB |
| `IX_Shipments_OriginBranchCode` → `IX_Shipments_OriginBranchCode_ZoneCode_ServiceLevel` | 489 pages (3.82 MB) | 560 pages (4.38 MB) | +0.56 MB |
| `IX_IdentityVerifications_MoneyOrderId` | 39 pages | 36 pages | −0.02 MB |
| **Total** | | | **+2,958 pages ≈ 23.1 MB** |

- **Share of the database:** the database uses 279.7 MB with the indexes, so they are about 9% of it (+9% over the 256.6 MB without them).
- **`IdentityVerifications` got smaller.** The old index was built row by row during the load. `CREATE INDEX` builds a compact one, which more than offsets the added `Outcome` column.
- **Small drift in other indexes** between the two files (for example `PK_CounterTransactions` 3,671 → 3,672 pages) comes from the rolled-back write batches, which leave split pages behind.

## Generated SQL (`ToQueryString`)

Captured by [`GeneratedSqlTests`](tests/ApexPost.Infrastructure.Tests/Sql/GeneratedSqlTests.cs) from the query builders the application itself uses. The tests also pin the parts the indexes depend on.

To regenerate:
```
dotnet test tests/ApexPost.Infrastructure.Tests --filter GeneratedSqlTests --logger "console;verbosity=detailed"
```

**1. Branch dashboard** (`EfBranchQueries.DashboardQuery`): one round trip, three correlated counts. The enum constant becomes the literal `'Paid'`, and the date filter is a half-open range.

```sql
DECLARE @branchCode varchar(48) = 'BR-0001';
DECLARE @branchCode0 varchar(48) = 'BR-0001';
DECLARE @start datetime2 = '2026-10-15T00:00:00.0000000Z';
DECLARE @end datetime2 = '2026-10-16T00:00:00.0000000Z';
DECLARE @branchCode1 varchar(48) = 'BR-0001';
DECLARE @lowStockThreshold int = 10;
DECLARE @branchCode2 varchar(48) = 'BR-0001';

SELECT [b].[Name], [b].[Region], [b].[OpeningHours], [b].[IsOpen], (
    SELECT COUNT(*)
    FROM [Counters] AS [c]
    WHERE [c].[BranchCode] = @branchCode AND [c].[IsOpen] = CAST(1 AS bit)), (
    SELECT COUNT(*)
    FROM [CounterTransactions] AS [c0]
    WHERE [c0].[BranchCode] = @branchCode0 AND [c0].[Status] = 'Paid' AND [c0].[OccurredAtUtc] >= @start AND [c0].[OccurredAtUtc] < @end), (
    SELECT COUNT(*)
    FROM [BranchStock] AS [b0]
    WHERE [b0].[BranchCode] = @branchCode1 AND [b0].[Quantity] <= @lowStockThreshold)
FROM [Branches] AS [b]
WHERE [b].[BranchCode] = @branchCode2
```

**2. Shipping volume** (`EfBranchReportQueries.ShippingVolumeQuery`): the grouping happens in SQL Server, and `varchar(48)` parameters match the column type.

```sql
DECLARE @branchCode varchar(48) = 'BR-0001';

SELECT [s].[ZoneCode], [s].[ServiceLevel], COUNT(*)
FROM [Shipments] AS [s]
WHERE [s].[OriginBranchCode] = @branchCode
GROUP BY [s].[ZoneCode], [s].[ServiceLevel]
```

**3. Money-order verification report** (`EfBranchReportQueries.MoneyOrderOutcomesQuery`): the optional navigation becomes a LEFT JOIN, and an order without a verification row is grouped under NULL (pending).

```sql
DECLARE @branchCode varchar(48) = 'BR-0001';

SELECT [i].[Outcome], COUNT(*)
FROM [MoneyOrders] AS [m]
LEFT JOIN [IdentityVerifications] AS [i] ON [m].[MoneyOrderId] = [i].[MoneyOrderId]
WHERE [m].[BranchCode] = @branchCode
GROUP BY [i].[Outcome]
```

## Small data: why the demo database still scans

On the demo database every table fits in 2–10 pages; for example, `BranchStock` is 150 rows in 4 pages. The same queries were run there with the indexes in place:

| Workload (BR-NORTH) | Plan | Reads |
|---|---|---|
| W1 low stock | Seek on `AK_BranchStock_BranchCode_ProductCode` + 30 key lookups, nested loops into `Products` | 62 on `BranchStock` |
| W2 daily sales | Seeks on both new indexes | 10 |
| W3 shipping volume | Seek on the composite | 2 |
| W4 money orders | Seeks on `IX_MoneyOrders_BranchCode` and the covering verification index | 30 |

**Why W1 doesn't use the new index on the demo data**
- The business key already returns rows in the report's `ORDER BY BranchCode, ProductCode` order. The demo plan therefore needs no Sort; the plan on `ApexPostDb_Perf` uses the new index and sorts 20 rows.
- With 4 pages, the difference between the two plans is a few dozen in-memory page reads, and the optimizer's cost estimates for them are almost equal.

Plan choices on a few hundred rows say little about production. That is why every measurement in this document uses the 200,000-transaction data set.

## Deploying the migration

- **Locks during the build:** `CREATE INDEX` and `DROP INDEX` run offline by default and block writes to the table while the index is built.
- **On a busy production database:** apply it in a quiet period, or add `.IsCreatedOnline()` to the five `HasIndex` calls. That option produces `WITH (ONLINE = ON)` and needs an edition that supports online index operations. It is not set by default because the production edition is not known here.
- **Brief gap on the replaced indexes:** the migration drops and recreates the two FK indexes it widens, under the same names. EF Core runs the whole migration in one transaction, so the unique index on `IdentityVerifications.MoneyOrderId` cannot be missing between the two steps.

## Conclusions

1. **Five index changes** (two new indexes, two widened FK indexes and one replaced FK index) remove every scan of the large tables (`BranchStock`, `CounterTransactions`, `TransactionLines`, `Shipments`) from the per-branch screens and reports.
   - The dashboard, which runs on every dashboard view, fell from 4,600–6,100 logical reads to about 20.
   - The daily sales report for the busiest branch fell from 20,885 to 371.
2. **Order the key columns for the query, not by habit.**
   - Equality columns first, then the range (`BranchCode, Quantity`; `BranchCode, OccurredAtUtc`), then the `GROUP BY` order.
   - Low-selectivity filters such as `Status` belong in INCLUDE, not in the key.
3. **Covering indexes also cure parameter sensitivity.** With no lookups or sorts left in the plan, one cached plan is right for a 400-row branch and a 20,000-row branch alike.
4. **An index cannot fix a bad estimate.** The daily-sales procedure needed a code change, removing local variables from the predicate, before the new index was used well.
5. **The price is paid on writes.**
   - About 30% more log for a typical write mix, mostly because `Quantity` is a key column of the stock index.
   - Five extra index row operations per two-line sale.
   - About 23 MB (9%) of storage.

   For a counter system where every dashboard view and report read the whole stock and sales tables, this trade is clearly worth it. The stock index's write cost is the first thing to revisit if stock updates ever dominate the workload.

## Reproducing

```powershell
# 1. Create the database from the migrations, at the pre-index migration, and load it
dotnet ef database update AddStoredProcedures --project src/ApexPost.Infrastructure --connection "Server=.;Database=ApexPostDb_Perf;Trusted_Connection=True;TrustServerCertificate=True"
sqlcmd -S . -E -d ApexPostDb_Perf -I -i database\perf\01-load-perf-data.sql

# 2. Baseline
.\database\perf\Measure-Workloads.ps1 -Phase baseline
sqlcmd -S . -E -d ApexPostDb_Perf -I -i database\perf\02-parameter-sensitivity.sql
sqlcmd -S . -E -d ApexPostDb_Perf -I -i database\perf\03-index-storage.sql
sqlcmd -S . -E -d ApexPostDb_Perf -I -i database\perf\04-write-overhead.sql

# 3. Apply the index and procedure migrations and measure again
dotnet ef database update --project src/ApexPost.Infrastructure --connection "Server=.;Database=ApexPostDb_Perf;Trusted_Connection=True;TrustServerCertificate=True"
.\database\perf\Measure-Workloads.ps1 -Phase final
# ...then the same three sqlcmd scripts
```

Open the `.sqlplan` files in SSMS to see the graphical plans with estimated and actual rows. The `E1-…` plans come from the parameter-sensitivity script: run it in SSMS with "Include Actual Execution Plan" on (Ctrl+M) and look at step 2.
