# ADR 0015: Repository and Unit of Work boundaries, and where direct DbContext projections are acceptable

Status: Accepted. Supersedes the repository part of ADR 0002.

## Context

In Week 2 every JSON repository saved its own file, so a checkout wrote several files with no shared transaction. Week 3 moves to EF Core, which already provides a unit of work (`DbContext`) and a generic repository (`DbSet`). The brief asks for:

- specific repositories that only stage changes and never return `IQueryable`;
- an `IUnitOfWork` with one commit per use case;
- query services for reads.

The decision is where each responsibility sits, and what the read side may do that the write side may not.

## Decision

### Write side: repositories stage, the unit of work commits

- **One specific repository per aggregate**, with the interface in Application and the implementation in Infrastructure: `IBranchRepository`, `ICounterRepository`, `IStaffMemberRepository`, `IProductRepository`, `IInventoryRepository`, `IShipmentRepository`, `IMoneyOrderRepository`, `ICounterTransactionRepository`.
- **Repositories only find and stage.** A repository finds an aggregate by its business key and offers `Add` and `Update`.
  - It never calls `SaveChanges`.
  - It never returns `IQueryable` or lists. Screens read through query services instead.
  - `Update` requires the aggregate to be tracked by the request's context (`Staging.EnsureTracked`), so a detached copy cannot overwrite newer data unnoticed.
- **`IUnitOfWork` commits** (implemented by `EfUnitOfWork`):
  - **`SaveChangesAsync`** commits everything the request staged in **one** `SaveChanges`, so EF Core runs it in one database transaction. It also translates database errors into application errors: `DbUpdateConcurrencyException` becomes `ConcurrencyConflictException`, and SQL errors 2601/2627 become `DuplicateKeyException`. Both map to 409 (ADR 0018).
  - **`ExecuteInTransactionAsync`** is for the use cases that mix `SaveChangesAsync` with a stored procedure or `ExecuteUpdate`, such as a first stock delivery. It opens one explicit transaction inside the SQL Server execution strategy, so a transient failure retries the whole unit.
- **One commit per use case.** Application services take repositories plus `IUnitOfWork` and commit once at the end.
  - Checkout commits the stock decrement, the sale, its lines and the payment in one `SaveChangesAsync`.
  - Application tests prove it with `SpyUnitOfWork`, which records commits and can fail them.
- **Scoped lifetimes.** `ApexPostDbContext`, every repository, the unit of work and the services are scoped. A request shares one context, and nothing static or singleton holds a context.

### Read side: query services may project from the DbContext

- **Screens and reports go through query interfaces** in Application (`IBranchQueries`, `ICatalogueQueries`, `ITransactionQueries`, `IStaffQueries`, `IShippingQueries`, `IMoneyOrderQueries`, `IBranchReportQueries`, `IBranchReportPageQueries`) that return read models (records). Every screen that only displays data reads this way, including the shipping label and the money-order verification page; a repository loads an aggregate only to change it.
- **The Infrastructure implementations read `ApexPostDbContext` directly** with `AsNoTracking()` projections, or call the stored procedures.
- **This direct access is acceptable here and nowhere else**, under these rules:
  - **Read-only:** a query service never changes data and holds no business rule. Rules live in Domain and Application services.
  - **One statement per query:** each method sends one SQL command that returns only the columns the screen shows, aggregated in SQL Server. Tests count the commands with an interceptor (no N+1), and EF command logging is on in Development.
  - **Contained:** EF types and `IQueryable` never leave Infrastructure. The internal query builders are visible to tests only, to capture `ToQueryString()` output (`PERFORMANCE.md`).
- **Set-based writes are separate ports.** `ICatalogueBulkUpdates` (`ExecuteUpdateAsync` price revision) and `IStockAdjustments` (`usp_AdjustStock`) are Application interfaces for commands that touch many rows or must run in the database. They are not hidden inside a repository.

### Query/command separation in one database

Reads and writes are separated in code: query services versus repositories plus the unit of work. They use the **same** SQL Server database and the same context type (the parallel reports page takes short-lived contexts from a factory, ADR 0021). No separate read database, replication or eventual consistency is needed: every query runs in milliseconds on 200,000 sales (`PERFORMANCE.md`), and a screen must show what was just committed.

## Options rejected

- **A generic repository exposing `IQueryable`:** it leaks EF Core into Application, lets any caller compose unbounded queries, and makes N+1 queries easy.
- **Repositories that save:** a checkout would commit several times, and a failure between commits would leave a half-recorded sale. That is the Week 2 compensation problem.
- **Reading screens through repositories and mapping in memory:** it loads whole aggregates (with their lines) to show a few columns.
- **A separate read store (full CQRS):** it adds synchronisation and staleness with no measured need.

## Consequences

- **Atomic and testable writes.** A use case's changes commit together or not at all, and Application tests run without a database.
- **Small reads.** Reads are fast and easy to tune with indexes, because each is one known SQL statement.
- **More interfaces.** There are query interfaces and read-model records next to the repositories; that is the price of keeping EF Core out of Application.
- **Repositories cannot be used for screens.** A new screen needs a query-service method, not a repository list method.
