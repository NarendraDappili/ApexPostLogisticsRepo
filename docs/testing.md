# Testing

## Current test projects

The solution contains three xUnit test projects:

- `ApexPost.Domain.Tests` references `ApexPost.Domain`.
- `ApexPost.Application.Tests` references `ApexPost.Application`.
- `ApexPost.Web.IntegrationTests` references `ApexPost.Web`.

`ApexPost.Web.IntegrationTests` also references the approved `Microsoft.AspNetCore.Mvc.Testing` package for future ASP.NET Core test hosting. Domain Tests now cover valid construction, normalization, invalid identifier inputs, non-negative monetary amounts, positive parcel measurements, decimal precision, and value equality for the Step 3 value objects. Application and Web test projects retain their template tests because their corresponding features remain planned.

## Planned coverage

Later steps should add focused unit tests for domain invariants and application use cases, plus web integration tests for relevant HTTP behavior. Tests should verify the assignment rules described in [business-rules.md](business-rules.md) and security-sensitive behavior described in [security.md](security.md) as those features are implemented.

## Commands

From the solution root:

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build
```
