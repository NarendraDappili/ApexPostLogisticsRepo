# ADR 0022: How the Week 2 business-rule tests carry into Week 3

Status: Accepted.

## Context

The brief says the Week 2 business-rule tests must keep passing unchanged. Week 3 replaced the JSON persistence with EF Core and SQL Server:
- **Repositories only stage changes.** The `SaveAsync` methods became `Add`/`Update`, and the unit of work commits ([ADR 0015](0015-repository-and-unit-of-work-boundaries.md)).
- **Optimistic concurrency and one commit per use case** replace compensation and branch locks ([ADR 0017](0017-optimistic-concurrency-for-stock-and-transactions.md)).
- **The reports aggregate in SQL Server**, through `IBranchReportQueries` and two stored procedures, instead of in memory in an Application `BranchReportService` ([ADR 0012](0012-stored-procedures-for-reports-and-stock-adjustment.md)).

Some Week 2 tests therefore could not compile, or tested behaviour that no longer exists. That decision was only in a code comment; this ADR records it.

## What happened to each Week 2 test

Compared between the Week 2 merge (`b30c46a`) and now. Domain tests: all unchanged. Application tests: 57 in Week 2.

| Week 2 tests | Count | What changed | Assertions |
|---|---|---|---|
| Unchanged | 44 | Nothing | Unchanged |
| Rewired | 9 | The services take an `IUnitOfWork`, so the fakes gain a `SpyUnitOfWork`. A failure that Week 2 injected into a repository's `SaveAsync` is now injected at the commit (`SpyUnitOfWork.FailWith`), because repositories no longer save. | **Unchanged.** Same assertions, same count, same expected values |
| Behaviour replaced | 2 | `Failed_transaction_save_compensates_previously_removed_inventory` and `Cancelled_transaction_save_still_compensates_previously_removed_inventory` checked that a failed save **put the stock back by hand**. Compensation no longer exists: stock, sale, lines and payment are one commit that either happens or does not. | Replaced, see below |
| Moved to SQL Server | 2 | `BranchReportServiceTests` tested the Application report service, which was deleted when the reports moved into SQL. | Ported with the **same data and the same expected figures**, see below |

**The two compensation tests** became `Failed_commit_persists_nothing_and_raises_no_sale_events` and `Cancelled_commit_persists_nothing_and_raises_no_sale_events`. They keep the business rule ("a failed or cancelled sale leaves no trace"):
- the commit was attempted once and did not succeed;
- no sale event was raised.

The database half of the old assertion ("the stock is back to 5") is now proven against a real database:
- `CheckoutPersistenceTests.A_sale_that_fails_part_way_through_the_commit_rolls_back_stock_and_transaction_rows_together` makes the last insert fail after the stock `UPDATE` reached SQL Server. It then checks that stock is still 5 and that no sale row exists.

**The two report tests:**
- **`Reports_group_actual_branch_data_without_identity_details`** became `QueryServiceTests.The_week_2_report_scenario_gives_the_same_figures_from_sql_server`.
  - **The same scenario is seeded into SQL Server:** one paid sale on the day (10.00 retail, 8.00 service, 1.80 tax), one the next day, one at another branch; stock of 2 and 9; two express shipments to zone 1 and one standard to zone 2; a verified and a failed money order, plus one at another branch.
  - **Its ten assertions are kept as they were,** so the same figures must come out of the stored procedures and LINQ projections.
  - **"Without identity details"** is now also checked structurally: the report type carries no document reference.
- **`Reports_propagate_cancellation`** is covered by `QueryServiceTests.Daily_sales_include_the_tax_charged_and_every_report_honours_cancellation`, which checks all four reports, not only daily sales.

## Decision

- **Week 2 business assertions are kept,** either unchanged or with their expected values carried over, wherever the behaviour still exists.
- **Only wiring may change in a kept test:** constructor arguments, fake shapes, and where a failure is injected.
- **When the behaviour itself is replaced by a Week 3 decision,** the test is replaced by one for the new rule. The decision that replaced it is named in an ADR (here ADR 0017), and the database half is proven by an integration test.
- **When the code under test moves to another layer,** the scenario moves with it, as with the reports moving to SQL Server.

## Consequences

- Week 2 business rules still have the same expected values, now checked against real SQL Server where the logic moved there.
- The Application test count rose from 57 to 67. The added tests cover the unit of work, stock adjustment and price revision.
- **A future change that touches a Week 2 test follows the same rule:** wiring only, or an ADR that names the replaced behaviour and where it is now proven.
