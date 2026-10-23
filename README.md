# ApexPost Logistics

ApexPost Logistics is a .NET 10 solution for a maintainable postal, shipping, and retail-counter platform. It is being built incrementally from the assignment specification; this repository currently contains the Step 2 solution architecture and template application only.

## Status

**Completed**

- Seven-project solution with the intended layer and test-project boundaries.
- .NET 10, C# 14, nullable-reference-type, and implicit-using configuration.
- Minimal ASP.NET Core MVC template application and placeholder xUnit test projects.
- Architecture, business-rule, security, and testing documentation.

**In progress**

- No business functionality is in progress as part of this step.

**Planned**

- Domain model and business invariants.
- Application services and repository abstractions.
- JSON persistence, dependency-injection registrations, MVC workflows, authentication, and integration tests.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Solution structure

```text
ApexPost.slnx
src/
  ApexPost.Domain/            Core business model (not yet implemented)
  ApexPost.Application/       Use cases and abstractions (not yet implemented)
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
