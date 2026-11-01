# ADR 0020: Single queries by default, split queries only for several collections

Status: Accepted.

## Context

EF Core can load an entity and a collection navigation in two ways:
- **Single query** (the default): one statement that JOINs the collection. The parent's columns repeat on every child row.
- **Split query** (`AsSplitQuery()`): one statement for the parents and one more for each collection. Nothing repeats, but every extra statement is another round trip, and each one repeats the parents' filter.

The brief lists split queries explicitly. The question is which one each query in ApexPost should use, decided from evidence.

**Where the application loads a collection.** Read screens use no-tracking projections that select only the columns shown, so they never load one ([ADR 0015](0015-repository-and-unit-of-work-boundaries.md)). The write side loads tracked aggregates:
- **A sale** (`CounterTransaction`): the header, one owned collection (its lines) and a one-to-one `Payment` row. EF Core includes owned navigations automatically.
- **A money order:** at most one identity verification (a reference, not a collection).
- **No aggregate has two collection navigations.** That is the case where a single query multiplies rows: 10 lines × 10 payments would return 100 rows.

## Measurement

`GeneratedSqlTests.Sales_with_their_lines_are_one_joined_statement_by_default_and_two_statements_when_split` loads a branch's sales with their lines both ways. It checks that both return the same aggregates, and prints the SQL.

**Single query:**

```sql
SELECT [c].[CounterTransactionId], … , [c].[Tax], [p].[CounterTransactionId], [t].[TransactionLineId], … , [t].[UnitPrice], [p].[Amount], …
FROM [CounterTransactions] AS [c]
LEFT JOIN [Payments] AS [p] ON [c].[CounterTransactionId] = [p].[CounterTransactionId]
LEFT JOIN [TransactionLines] AS [t] ON [c].[CounterTransactionId] = [t].[CounterTransactionId]
WHERE [c].[BranchCode] = @data_Branch
ORDER BY [c].[TransactionCode], [c].[CounterTransactionId], [p].[CounterTransactionId]
```

**Split query (`AsSplitQuery()`):**

```sql
SELECT [c].[CounterTransactionId], … , [c].[Tax], [p].[CounterTransactionId], [p].[Amount], …
FROM [CounterTransactions] AS [c]
LEFT JOIN [Payments] AS [p] ON [c].[CounterTransactionId] = [p].[CounterTransactionId]
WHERE [c].[BranchCode] = @data_Branch
ORDER BY [c].[TransactionCode], [c].[CounterTransactionId], [p].[CounterTransactionId]

SELECT [t0].[TransactionLineId], … , [t0].[UnitPrice], [c].[CounterTransactionId], [p].[CounterTransactionId]
FROM [CounterTransactions] AS [c]
LEFT JOIN [Payments] AS [p] ON [c].[CounterTransactionId] = [p].[CounterTransactionId]
INNER JOIN [TransactionLines] AS [t0] ON [c].[CounterTransactionId] = [t0].[CounterTransactionId]
WHERE [c].[BranchCode] = @data_Branch
ORDER BY [c].[TransactionCode], [c].[CounterTransactionId], [p].[CounterTransactionId]
```

**Measured** on the Development database (`ApexPostDb`, SQL Server 2025), branch BR-NORTH: 50 sales with 88 lines (1.76 lines per sale). Warm cache, `SET STATISTICS IO ON`. Both statements were run exactly as EF Core sends them.

| | Single query | Split query |
|---|---|---|
| Round trips | **1** | 2 |
| Rows returned | 88 | 50 + 88 = 138 |
| Values returned (rows × columns) | 88 × 30 = 2,640 | 50 × 21 + 88 × 11 = 2,018 |
| Logical reads | **383** (lines 276, payments 100, sales 7) | **471** (12 for statement 1; 459 for statement 2, which reads the sales and payments again) |

## Decision

1. **Single query is the default and stays implicit.** For the one collection the model has, it makes one round trip and fewer reads.
   - The split query sends 24% fewer values, because the header is not repeated on each line.
   - But it reads 23% more pages, because its second statement must find the same sales again.
   - It also needs a second round trip.
   - The repeated header is small with fewer than two lines per sale.
2. **`AsSplitQuery()` is required on any query that includes two or more collection navigations,** or one collection with many wide rows per parent (dozens per row). That is when repetition outgrows the extra statements. Neither case exists today (see Context).
3. **Screens never load collections.** They project, as before. This ADR concerns tracked aggregates only.
4. **Consistency is not a reason to split here.** With split queries, another transaction can commit between the statements. The database runs with `READ_COMMITTED_SNAPSHOT`, so each statement sees a consistent snapshot, but the two snapshots can differ. A single query cannot have that gap. This matters for a sale, whose lines and header must agree.

## Consequences

- No production query uses `AsSplitQuery()` today. The decision rests on the measurement above, not on habit. The test pins both SQL shapes, so a change to the model or to EF Core that alters them shows up.
- If an aggregate gains a second collection, its repository query must say `AsSplitQuery()` (or `AsSingleQuery()` with a measured reason) in the same change. The reviewer checks it with `ToQueryString`, as in `GeneratedSqlTests`.
- The numbers are from a small database. The *ratio* is what matters: it depends on lines per sale, not on table size. PERFORMANCE.md keeps the method, so it can be repeated on the 200,000-sale data set.
