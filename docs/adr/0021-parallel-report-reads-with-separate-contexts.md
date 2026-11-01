# ADR 0021: Parallel report reads, one DbContext per report

Status: Accepted. Brings the Week 2 `Task.WhenAll` pattern ([ADR 0006](0006-parallelize-only-independent-checkout-reads.md)) back where it is safe with EF Core.

## Context

In Week 2, checkout started its independent validation reads together and awaited them with `Task.WhenAll` (ADR 0006).

[ADR 0017](0017-optimistic-concurrency-for-stock-and-transactions.md) made those reads sequential. They now share the request's scoped `DbContext`, and a `DbContext` runs one operation at a time: a second query started while the first is running throws `InvalidOperationException`.

The reports page needs four reports for one branch:
- daily sales (`usp_DailyBranchSales`);
- low stock (`usp_LowStockReport`);
- shipping volume (a LINQ `GROUP BY`);
- money-order verification (a LINQ `LEFT JOIN` and `GROUP BY`).

These reports are independent:
- none uses another's result;
- none writes;
- none needs to see the others' data at the same instant.

Sequentially, the page waits for the sum of four round trips. In parallel, it waits for the slowest one.

## Options

1. **Keep the reads sequential** on the request's context. Simple and correct, but the latency adds up.
2. **`Task.WhenAll` on the request's context.** Not allowed: EF Core's concurrency detector throws.
3. **`Task.WhenAll` with one short-lived context per report**, taken from `IDbContextFactory<ApexPostDbContext>`.

## Decision

Option 3, for the reports page only.

- **The port is in Application.** `IBranchReportPageQueries.GetAsync` returns the four reports as one `BranchReportPage`; `ReportsController` calls it once.
- **The implementation is in Infrastructure.** `ParallelBranchReportPageQueries` starts the four reports together and awaits them with `Task.WhenAll`.
  - Each report creates its own context with `CreateDbContextAsync`, runs the same `EfBranchReportQueries` code as before, and disposes the context.
  - Each context therefore has its own pooled connection. Nothing is shared between the parallel reads.
- **The factory is a separate registration** in `Program.cs`: a `PooledDbContextFactory` with `NoTracking` as the default, the same connection string and retry policy, and the application's logger factory, so the SQL is still logged in Development.
  - It does not include the audit-column interceptor. These contexts only read, so they never stamp audit columns.
- **The request's scoped context is untouched.** Repositories, the unit of work and every write still share one context per request (ADR 0015, ADR 0017).
- **Errors:** if a report fails, `Task.WhenAll` faults and the first failure is rethrown. A business error, such as an unknown branch from `usp_DailyBranchSales`, still becomes a 400 Problem Details response.
- **Cancellation:** the request's token flows into every report.

**Where it is not used:**
- **Checkout and other writes.** They need one context, one unit of work and one transaction; parallel contexts would not share the transaction.
- **Single-query screens.** There is nothing to overlap.

## Consequences

- **The page waits for the slowest report** instead of the sum of all four.
- **Each page view uses up to four connections at once** instead of one. The connection pool (default 100) allows that.
  - Under heavy load the parallelism can be reduced, or made sequential, inside the one class without touching the controller.
- **The four reports can see slightly different moments:** each runs in its own statement and snapshot. That is acceptable for a management report. Each report is internally consistent, and none depends on another.
- **Tests:** `ParallelReportTests` proves the concurrency, not just the result.
  - A gated context factory hands out contexts only when all four reports are waiting at the same time. Run one after another, the test would time out.
  - It checks that four distinct contexts are created and disposed.
  - It checks that the results equal the sequential reads.
  - It checks that a failing report fails the page with its business message.
