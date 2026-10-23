# ApexPost Logistics — GitHub Copilot Project Instructions

## Project objective

Build ApexPost Logistics from scratch as a maintainable postal, shipping and retail counter platform. Do not refactor or reuse the Week 1 project.

## Technology constraints

- .NET 10 and C# 14.
- Nullable reference types enabled.
- ASP.NET Core MVC with Razor Views and selected Razor Pages.
- JSON files for persistence; no database or ORM in the initial assignment.
- Built-in Microsoft.Extensions dependency injection, configuration and logging.
- xUnit and Microsoft.AspNetCore.Mvc.Testing for tests.
- Use only approved Microsoft.AspNetCore.*, Microsoft.Extensions.*, xUnit and Microsoft.AspNetCore.Mvc.Testing libraries unless explicitly authorized.

## Architecture

Use these projects:

- ApexPost.Domain
- ApexPost.Application
- ApexPost.Infrastructure
- ApexPost.Web
- ApexPost.Domain.Tests
- ApexPost.Application.Tests
- ApexPost.Web.IntegrationTests

Dependency rules:

- Domain must not depend on other solution projects.
- Application may depend on Domain.
- Infrastructure may depend on Application and Domain.
- Web may depend on Application and Infrastructure for composition-root registration.
- Tests reference only the projects they need.
- Program.cs contains dependency registration and HTTP pipeline configuration, not business logic.
- Controllers handle HTTP concerns; application services coordinate use cases; domain objects enforce business invariants.
- Repository interfaces live in Application and JSON implementations live in Infrastructure.

## Mandatory business requirements

Support branches and counters, staff and roles, product catalogue, branch inventory, shipping, money orders, identity verification, mixed retail/service transactions, pricing, tax, voids, refunds, reports and simulated notifications.

Roles: Counter Clerk, Branch Manager, Auditor and Administrator.

Enforce these invariants:

1. Stock must never become negative.
2. Inactive staff cannot process transactions.
3. Clerks can transact only at their assigned branch.
4. High-value money orders require successful identity verification in the same transaction.
5. Shipments exceeding the configured service-level weight limit must be rejected.
6. Voids and refunds require Branch Manager authorization and a reason.
7. Closed branches cannot accept transactions.

## Engineering rules

- Apply SOLID and use patterns only when they solve a real problem.
- Keep business logic independent of JSON, MVC and HTTP.
- Use specific interfaces for inventory, transactions, audit, shipping and notification operations.
- Use decimal for monetary calculations and explicit rounding/tax policies.
- Use async/await for I/O and propagate CancellationToken.
- Never use .Result, .Wait() or Thread.Sleep.
- Avoid fire-and-forget work for critical business events.
- Protect JSON writes from concurrent access and avoid partial-file corruption.
- Do not assume writes to multiple JSON files are atomic.
- Do not store full identity document numbers, passwords, API keys or payment-card data in audit logs.
- Do not commit secrets or real credentials.
- Use dedicated view models and antiforgery protection for MVC form posts.
- Never rely on UI visibility alone for authorization.

## Incremental implementation protocol

For each requested implementation step:

1. Inspect the existing solution and current Git changes.
2. Implement only the requested step and its immediate prerequisites.
3. Do not prematurely implement later roadmap steps.
4. Preserve working code and avoid unrelated refactoring.
5. Restore packages only when necessary.
6. Build the entire solution using:
   dotnet build ApexPost.slnx --no-restore

   If restore is required, first run:
   dotnet restore ApexPost.slnx

7. Run all relevant tests, then run:
   dotnet test ApexPost.slnx --no-build

   Use --no-build only after the latest solution build succeeds and the test assemblies are up to date.

8. If anything fails, diagnose and fix issues caused by the current changes. Repeat build and tests.
9. Do not suppress warnings, disable nullable checks, skip tests, or weaken business rules just to obtain a successful build.
10. Do not claim a build or test passed unless you actually executed the command and observed success.
11. If a failure is unrelated or an environment prerequisite is missing, explain it clearly.
12. Finish with a concise report: files changed, implementation delivered, commands run, results, known limitations and suggested Git commit message.

Do not begin the next implementation step until the current step passes its required validation.