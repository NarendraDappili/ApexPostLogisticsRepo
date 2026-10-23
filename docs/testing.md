# Testing

## Current test projects

The solution contains three xUnit test projects:

- `ApexPost.Domain.Tests` references `ApexPost.Domain`.
- `ApexPost.Application.Tests` references `ApexPost.Application`.
- `ApexPost.Web.IntegrationTests` references `ApexPost.Web`.

`ApexPost.Web.IntegrationTests` also references the approved `Microsoft.AspNetCore.Mvc.Testing` package for future ASP.NET Core test hosting. Domain Tests cover value-object construction, normalization, invalid inputs, decimal precision, and value equality. They also cover branch and counter state, staff activity and branch assignment, product price updates, non-negative inventory, shipment composition, money-order verification outcomes, and entity identity equality. Step 5 adds declaration-only Application contracts, so no artificial contract-existence tests or fake implementations were added; Application Tests retains its template test until Application behavior exists. Web Tests also retain their template test.

## Planned coverage

Later steps should add focused unit tests for domain invariants and application use cases, plus web integration tests for relevant HTTP behavior. Tests should verify the assignment rules described in [business-rules.md](business-rules.md) and security-sensitive behavior described in [security.md](security.md) as those features are implemented.

## Commands

From the solution root:

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build
```
