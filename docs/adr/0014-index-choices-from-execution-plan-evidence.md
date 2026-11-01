# ADR 0014: Choose indexes from execution-plan evidence

Status: Accepted.

## Context

`InitialCreate` contains only the indexes the model requires: primary keys, alternate keys, unique rules, and one index per foreign key (EF Core's convention). The screens and reports filter and group by other columns.

Adding indexes "because a column is filtered" costs storage and slows every write, so each index must earn its place. The demo database (242 sales) is too small to show what any index does.

## Decision

### How indexes are chosen

1. Measure first. `ApexPostDb_Perf` is built from the same migrations and loaded by `database/perf/01-load-perf-data.sql`: 200,000 sales in one year, a skewed branch distribution, 528,573 lines. Its baseline covers the real workloads: actual plans, logical reads and CPU (`database/perf/Measure-Workloads.ps1`).
2. Design an index only for a measured problem. Justify its key order (equality columns first, then the range, then grouping order) and its INCLUDE columns.
3. Measure again, including the write overhead and the storage cost.
4. Keep an index only if the read gain clearly outweighs those costs.

### How indexes are declared

- **Fluent API only.** Every index is declared once, with `HasIndex(…)` in an `IEntityTypeConfiguration` class, plus `IsUnique`, `HasFilter` or `IncludeProperties` where needed.
- **Not the `[Index]` attribute.** It would put an EF Core attribute on the Domain entities, which reference nothing, and it cannot express INCLUDE columns.
- **No raw-SQL duplicates.**
- **Workload indexes come in a follow-up migration** (`AddWorkloadIndexes`), added after the baseline was captured. The path from configuration to migration, script and execution plan can be followed in one direction.
- **Pinned by a test:** `ApexPostModelTests` fixes each index's key order and INCLUDE list.

### The indexes chosen

| Index | Serves | Key order and INCLUDE, in short |
|---|---|---|
| `IX_BranchStock_BranchCode_Quantity` INCLUDE `ProductCode` | Low-stock report, dashboard | Branch equality, then the `Quantity <=` range |
| `IX_CounterTransactions_BranchCode_OccurredAtUtc` INCLUDE `Status, Subtotal, Tax` | Daily sales, dashboard count, recent transactions | Branch equality, then the date range. `Status` (96% Paid) is included rather than keyed. |
| `IX_TransactionLines_CounterTransactionId` INCLUDE `ProductCode, Quantity, UnitPrice` | Daily sales | The FK index made covering |
| `IX_Shipments_OriginBranchCode_ZoneCode_ServiceLevel` | Shipping volume | Branch, then the `GROUP BY` columns. It also replaces the single-column FK index. |
| `IX_IdentityVerifications_MoneyOrderId` INCLUDE `Outcome` | Money-order report | The unique FK index made covering |

### Rejected

- **Filtered low-stock index (`WHERE Quantity <= 10`):** the threshold is configuration and arrives as a parameter, and SQL Server cannot match a filtered index to a parameterised predicate.
- **Filtered `WHERE Status = 'Paid'` index:** it saves about 4% of rows and would no longer serve the recent-transactions list, which shows every status.
- **`(Quantity, BranchCode)`:** it reads every branch's low rows to find one branch's.

## Consequences

**Read gains (measured, in `PERFORMANCE.md`):**
- Dashboard: 6,126 → 20 logical reads.
- Daily sales for the busiest branch: 20,885 → 371.
- Low stock: 2,158 → 24.
- Shipping volume: 1,765 → 113.
- Money-order report: 150 → 69.

**Costs (measured):**
- About 23 MB of indexes (9% of the database).
- About 30% more transaction log for a typical write mix.
- Five extra index row operations per two-line sale, mostly because `Quantity` is a key column of the stock index.
- A one-off wave of page splits after the indexes are created.

**Other effects:**
- **No more parameter sensitivity:** the covering indexes remove the shipping report's sensitivity to plan reuse.
- **Not every problem is an index problem:** the daily sales procedure needed a code change (no local variables in the predicate) before the index was used well. That fix is the separate migration `RedeployDailyBranchSales`.
- **Deployment:** creating the indexes blocks writes on those tables while it runs. On a busy production database, apply the migration in a quiet period, or use online index creation where the edition supports it.
- **Future indexes follow the same process:** a measured workload, a written justification, and a follow-up migration.
