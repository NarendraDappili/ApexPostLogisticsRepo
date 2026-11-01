# Testing

## Commands

```powershell
dotnet build ApexPost.slnx -c Release -warnaserror
dotnet test ApexPost.slnx -c Release --no-build
```

The latest run had **261 tests passing, 0 failing, 0 skipped**, with a zero-warning build.

| Project | Tests | Needs a database |
|---|---|---|
| `ApexPost.Domain.Tests` | 66 | No |
| `ApexPost.Application.Tests` | 86 | No |
| `ApexPost.Infrastructure.Tests` | 59 | SQL Server (except the model and password-hasher tests) |
| `ApexPost.Web.IntegrationTests` | 50 | SQL Server |

Each test project references only the production project it tests. They use xUnit; the Web tests also use `Microsoft.AspNetCore.Mvc.Testing`.

## The test database

The SQL Server tests never touch the development database:

- **Disposable databases.** Each test run creates its own database, `ApexPostDb_Test_<guid>`. It is built with `Database.MigrateAsync()` from the **real migrations**, never `EnsureCreated`, and dropped afterwards. The test database therefore has exactly the production schema, procedures and indexes.
- **Shared within a run, isolated per test.** The Infrastructure tests share one database per run through an xUnit collection fixture. Each test seeds its own rows, with codes carrying a unique suffix, so tests never see each other's data.
- **One database per Web factory.** Each `WebApplicationFactory` gets its own database.
- **A guard.** It refuses any database whose name lacks the `ApexPostDb_Test_` prefix, so a misconfigured connection string cannot point the tests at real data.
- **Choosing the server.** It comes from `APEXPOST_TEST_SQLSERVER`, a connection string without a database name. The default is the local default instance with Windows authentication. CI starts a throwaway SQL Server container with a random password and sets this variable.

No test uses a real credential, external service or arbitrary delay. All data is fictional.

## What is covered

### Unit tests: no database

**Domain (66).** These are the Week 2 business-rule tests, which **pass unchanged** against the new persistence layer. They cover:
- value objects;
- stock that never goes negative;
- staff eligibility;
- the transaction state machine (pay, void a paid sale, refund);
- money-order verification and masked references;
- weight limits.

`PersistenceReadyDomainTests` cover the Week 3 additions: SKU, category, payment, stock adjustment, and the 30 kg limit.

**Application (86).** Services run against in-memory fake repositories and a `SpyUnitOfWork`. The spy records commits and explicit transactions, can fail a commit, and can cancel one. The tests show:
- **One commit per use case, or none on failure.** Checkout commits the stock, the sale, the lines and the payment once. A failed validation or a failed commit leaves nothing committed.
- **The same business assertions as Week 2.** Only the wiring changed (the spy unit of work), and every Week 2 business assertion is unchanged.
- **Stock adjustments.** There is one transaction per adjustment. A first delivery saves the stock row before the procedure runs. Only a manager of the same branch may adjust. The domain rules run first.
- **Price revision.** The percentage limits are enforced, and the request reaches the set-based port.

### The Week 2 tests after the refactor

The brief asks for the Week 2 business-rule tests to pass unchanged against the new persistence layer. Compared with the last Week 2 commit (`7548441`):

- **Domain:** every Week 2 test file is byte-for-byte unchanged. One file was added (`PersistenceReadyDomainTests`).
- **Application:** services now commit through `IUnitOfWork`, so the test wiring changed: fakes stage changes and a `SpyUnitOfWork` commits. The business assertions are unchanged.
- **Four tests were replaced.** Each tested a Week 2 mechanism that no longer exists. Its business rule is now proven where the rule now lives:

| Week 2 test (removed) | Mechanism it tested | Replaced by |
|---|---|---|
| `Failed_transaction_save_compensates_previously_removed_inventory` | Compensating stock writes after a failed JSON save | `Failed_commit_persists_nothing_and_raises_no_sale_events` (Application); `A_sale_that_fails_part_way_through_the_commit_rolls_back_stock_and_transaction_rows_together` (SQL Server) |
| `Cancelled_transaction_save_still_compensates_previously_removed_inventory` | Compensation ignoring request cancellation | `Cancelled_commit_persists_nothing_and_raises_no_sale_events` (Application) |
| `Reports_group_actual_branch_data_without_identity_details` | The in-memory `BranchReportService` | `Reports_are_aggregated_by_the_database_and_count_only_paid_sales_of_the_day` and `Daily_sales_include_the_tax_charged_and_every_report_honours_cancellation` (SQL Server). The report records hold counts only, never identity data. |
| `Reports_propagate_cancellation` | Same service | `Daily_sales_include_the_tax_charged_and_every_report_honours_cancellation`, covering all four reports |

### SQL Server integration tests (Infrastructure, 59)

| Area | Tests show |
|---|---|
| Model (`ApexPostModelTests`, no database) | Audit columns on every table; `rowversion` on stock and sales; masked identity column; unique business keys; weight CHECKs; only owned children cascade; decimal(18,2) and varchar codes; mapper round trip; **the workload indexes' key order and INCLUDE columns** |
| Repository mapping (`EfRepositoryMappingTests`) | Every aggregate round-trips with its value objects, owned types and audit columns. Value-object predicates become `varchar(48)` parameters. Duplicate keys become `DuplicateKeyException`, and a detached update is refused. |
| Atomicity and concurrency (`CheckoutPersistenceTests`) | A sale is one commit. **A failure part-way through the commit rolls back stock and sale rows together.** Two clerks selling the last unit leave stock at 0 and one conflict. **8 concurrent sales never oversell or lose an update.** Sales and voids lock stock rows in key order, before the sale rows (the deadlock rule). |
| Stored procedures (`StoredProcedureTests`) | Parameter validation errors. Both report modes. Adjustment and history in one transaction. Refusal below zero. A procedure update invalidates a stale EF copy. A mixed `SaveChanges` + procedure call rolls back as one unit. |
| Query services (`QueryServiceTests`) | Each screen query is **one SQL command** (no N+1) and tracks nothing. Reports aggregate in the database, count only paid sales of the day, include the tax charged, and honour cancellation. Price revision is one set-based `UPDATE`. |
| Generated SQL (`GeneratedSqlTests`) | Captures `ToQueryString()` for the dashboard, shipping volume and money-order reports (used in `PERFORMANCE.md`), and pins their SARGable shape. |
| Audit store (`EfAuditStoreTests`) | Round trip and staff stamping. A failed business save is audited but never committed by the audit write. A failed audit write does not undo a committed change. |
| Seeding and caching (`SeedingAndRateCacheTests`) | The seed meets the brief's volumes (5 branches, 20 staff, 30 products, 10 zones/60 rates, 240 sales). It refuses an unmigrated database. Rates are cached, but an empty table is not. |

### Web integration tests (50)

`WebApplicationFactory` hosts the real `Program.cs` against a disposable test database, with cookies and antiforgery tokens handled as a browser would. The tests cover:

- **What the brief requires:**
  - a sale posted through the counter screen is stored with its lines, payment and audit columns;
  - **a void requires the Branch Manager role** (a clerk gets 403);
  - a manager voids a paid sale and the stock is returned;
  - **the reports show the day's totals from the database**;
  - **errors return Problem Details**: 409 for two clerks selling the last unit and for a duplicate email, 404 for a missing sale, 400 for a broken rule or an invalid price revision.
- **Over-posting:** posting status, identity, activity, password or audit fields has no effect on a sale or a staff edit.
- **Workflows:** a several-line basket; shipments and verified money orders billed in the basket; weight limits and verification errors shown; manager stock adjustment through the procedure; administrator price revision; staff and branch administration.
- **Security pipeline:** sign-in with hashed passwords; inactive staff rejected and existing cookies invalidated; cross-branch denial; security headers and correlation ID; maintenance mode; API key; IP allowlist; shared integration rate limit; login rate limit; CORS; no exception details in Problem Details or logs; a client abort is not a 500.
- **Audit:**
  - safe fields only;
  - no password, document number or API key in records or logs;
  - an audit-store failure never breaks a committed sale;
  - **the recorded status is the one the client received**, for a sign-in redirect (302), an in-action `Forbid` (403), a mapped exception (404) and a 409 form. This test fails against the old action filter.

`ProblemDetailsMappingTests` cover each row of the exception mapping, including framework exceptions of the same types staying 500 without detail.

## Not covered

- **No browser automation.** There is no real-browser, screen-reader, keyboard-only or automated accessibility test.
- **No load or penetration test.** `PERFORMANCE.md` measures query cost with a reproducible data set; it is not a load test.
- **No proxy or multi-host deployment test.** The concurrency tests use several contexts and connections against one SQL Server. That reproduces the conditions of several web servers, but not an actual farm.
- **CI unverified.** The CI workflow, with its SQL Server container, has not run on GitHub yet; locally, the same commands pass.
