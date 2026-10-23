# ApexPost Logistics

ApexPost Logistics is a .NET 10 solution for a maintainable postal, shipping, and retail-counter platform. It is being built incrementally from the assignment specification; this repository currently contains the Step 6 solution architecture, domain value objects and core entities, Application contracts, configurable pricing rules, and template application.

## Status

**Completed**

- Seven-project solution with the intended layer and test-project boundaries.
- .NET 10, C# 14, nullable-reference-type, and implicit-using configuration.
- Validated immutable domain value objects for identifiers, monetary amounts, and parcel measurements, with focused xUnit coverage.
- Core domain entities for branches, counters, staff, catalogue products, inventory, shipments, and money orders, with local invariant coverage.
- Application-layer repository, audit, notification, and current-staff-context contracts, ready for future consumers and Infrastructure implementations.
- Deterministic retail and shipping pricing calculators using configurable in-memory rate data; configuration loading remains planned.
- Minimal ASP.NET Core MVC template application and placeholder application/web test projects.
- Architecture, business-rule, security, and testing documentation.

**In progress**

- No business functionality is in progress as part of this step.

**Planned**

- Transaction composition, tax, void/refund authorization, and other cross-entity business workflows.
- JSON-based pricing configuration loading and persistence.
- Application services and business workflows that consume the contracts.
- JSON persistence, dependency-injection registrations, MVC workflows, authentication, and integration tests.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Solution structure

```text
ApexPost.slnx
src/
  ApexPost.Domain/            Value objects and core entities; workflows planned
  ApexPost.Application/       Contracts and configurable pricing calculators; services planned
  ApexPost.Infrastructure/    Technical adapters and JSON persistence (not yet implemented)
  ApexPost.Web/               ASP.NET Core MVC composition root and UI template
tests/
  ApexPost.Domain.Tests/      Domain tests
  ApexPost.Application.Tests/ Application tests
  ApexPost.Web.IntegrationTests/ Web integration tests
```

## Architecture

Dependencies flow inward: Web depends on Application and Infrastructure; Infrastructure depends on Application and Domain; Application depends on Domain; Domain has no solution-project dependencies. Each test project references only its corresponding subject project.

See [DESIGN.md](DESIGN.md) for the architecture and planned request flow. Supporting requirements are documented in [business rules](docs/business-rules.md), [security](docs/security.md), and [testing](docs/testing.md).

## Build and test

Run these commands from the solution root:

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build
```
