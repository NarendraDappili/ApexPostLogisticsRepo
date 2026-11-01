# ADR 0017: Optimistic concurrency with rowversion for stock and transactions

Status: Accepted. Supersedes ADR 0002 (same-process locking), ADR 0006 (parallel checkout reads) and ADR 0007 (compensation after cancellation).

## Context

Two clerks can sell the last unit of a product at the same moment, and a manager can void a sale while another manager refunds it. In Week 2:

- a per-branch `SemaphoreSlim` serialised checkouts inside one process;
- per-file locks protected each JSON read-modify-write;
- a failed save was undone by compensating writes.

None of that works with several web servers, and the brief requires such a conflict to be **detected** and returned as HTTP 409.

## Decision

### Optimistic concurrency at the row level

- **Two tables carry a `rowversion` column** (shadow property `RowVersion`, `IsRowVersion()`): `BranchStock` and `CounterTransactions`. EF Core adds `WHERE RowVersion = @original` to every `UPDATE` of those rows.
- **A use case reads, decides in the domain, then commits once.**
  - Checkout loads the stock rows, removes stock through `BranchInventoryItem.RemoveStock` (which refuses to go below zero), and saves the stock updates, the sale, its lines and its payment in one `SaveChangesAsync`, so one database transaction.
- **A stale write is refused and nothing is half-saved.**
  - If another request changed a stock row or the sale after it was read, the `UPDATE` affects no row and EF Core throws `DbUpdateConcurrencyException`.
  - The transaction rolls back, so no stock is taken, no sale is stored, and no compensation is needed.
  - `EfUnitOfWork` turns the exception into `ConcurrencyConflictException`, which becomes 409: a Problem Details response, or a 409 page error on MVC forms.
- **No automatic business retry.** The user sees "someone else changed this data" and retries with fresh data. Retrying silently could sell against a quantity the clerk never saw.

### The database guards stock too

- `CK_BranchStock_Quantity` (`Quantity >= 0`) rejects a negative quantity from any source.
- `usp_AdjustStock` checks stock inside its `UPDATE` (`WHERE Quantity + @Delta >= 0`).
- Its update also changes the row's `rowversion`, so a clerk's stale EF copy of that row is rejected at commit (test `A_procedure_change_invalidates_an_ef_tracked_copy_through_the_rowversion`).

### Transactions and the execution strategy

- **One `SaveChangesAsync` is atomic** on its own.
- **Use cases that mix `SaveChangesAsync` with a stored procedure or `ExecuteUpdate`** (a first stock delivery) run inside `IUnitOfWork.ExecuteInTransactionAsync`: one explicit transaction inside the SQL Server execution strategy.
- **The execution strategy** (`EnableRetryOnFailure`) retries transient errors, including being chosen as a deadlock victim (1205), by replaying the whole unit.

### Readers do not block writers

The database runs with `READ_COMMITTED_SNAPSHOT` ON. Screens and reports read the last committed version and take no shared locks. The trade-off: a reader can see a value that is about to change. That is safe because no write is decided from such a read alone; the `rowversion` check catches it at commit.

### What was removed

| Week 2 mechanism | Why it is gone |
|---|---|
| Per-branch `SemaphoreSlim` around checkout | Process-local, so useless with two servers. It also serialised every sale of a branch and hid conflicts instead of reporting them. |
| Compensating stock writes after a failed save (ADR 0007) | The database transaction rolls back stock and sale together; there is nothing to compensate. |
| `Task.WhenAll` over the checkout's validation reads (ADR 0006) | One `DbContext` must not run two queries at once. The reads are now sequential, and the database makes them fast. |

### Deadlock avoidance

Every writer locks the stock rows first, in key order, and then the sale tables. Transactions are short: reads happen before them, and no lock is held across a request. The ordering is pinned by a test; see `sql/README.md`, "Avoiding deadlocks".

## Options rejected

- **Pessimistic locking (`UPDLOCK`/`SERIALIZABLE` reads, or `SELECT … FOR UPDATE`-style holds):** it would hold locks from the first read to the commit and raise the deadlock risk, for little gain: conflicts on the same stock row are rare, and a 409 is an acceptable outcome when they happen.
- **Application locks (`sp_getapplock`) or a distributed lock:** they serialise more than necessary and add an infrastructure dependency.
- **Last-writer-wins without a version check:** it loses updates. The second clerk's sale would overwrite the first clerk's stock decrement.

## Consequences

- **Correct across processes.** Stock never goes negative and no update is lost, however many web servers run. This is proven under real concurrency against SQL Server:
  - `Two_clerks_selling_the_last_unit_cannot_drive_stock_negative`;
  - `Concurrent_sales_from_separate_requests_never_oversell_or_lose_an_update` (8 concurrent sales of 3 units);
  - `A_stale_stock_read_is_rejected_as_a_concurrency_conflict`;
  - `Two_clerks_selling_the_last_unit_leave_one_sale_and_the_other_gets_a_409` (MVC).
- **Rare conflicts are reported.** Under contention for the same row, the losing clerk gets a 409 and must retry; the conflict is never silently resolved.
- **Clean failures.** A failed commit leaves nothing to reconcile, which removes the Week 2 reconciliation boundary.
