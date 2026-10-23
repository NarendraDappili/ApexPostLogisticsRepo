# Testing

## Current test projects

The solution contains three xUnit test projects:

- `ApexPost.Domain.Tests` references `ApexPost.Domain`.
- `ApexPost.Application.Tests` references `ApexPost.Application`.
- `ApexPost.Web.IntegrationTests` references `ApexPost.Web`.

`ApexPost.Web.IntegrationTests` also references the approved `Microsoft.AspNetCore.Mvc.Testing` package for future ASP.NET Core test hosting. Domain Tests cover value-object construction, normalization, invalid inputs, decimal precision, and value equality. They also cover branch and counter state, staff activity and branch assignment, product price updates, non-negative inventory, shipment composition, money-order verification outcomes, and entity identity equality. Application Tests now cover retail quantity calculations and configured shipping-rate selection, all three service levels, shipment type and zone selection, maximum-weight boundaries, missing/ambiguous rates, and invalid service levels. The shipping test rate values are controlled fixtures, not production configuration. Web Tests retain their template test.

## Planned coverage

Step 7 adds `ApexPost.Infrastructure.Tests`, which references Infrastructure and uses a unique directory below the OS temporary directory for every test. It cleans up only that test-owned directory. Coverage includes round trips for all seven repositories, identity-based update/upsert behavior, missing files, empty/malformed JSON, cancellation, sensitive-data-safe serialization, and concurrent same-process writes.

Step 8 adds seed-data tests using separate test-owned temporary directories. They verify the required 5 branches, 20 staff, 30 products, 10 zones/60 rates, and 40 historical records; role coverage, uniqueness, branch/staff/product/inventory relationships, rate construction through existing value objects, transaction arithmetic, rerun no-op behavior, and rejection of unmarked pre-existing data.

Step 9 adds Application inventory-service tests for available stock, insufficient stock, invalid quantities, branch isolation, positive/negative adjustments, low-stock crossings, and concurrent removals. Infrastructure tests exercise concurrent `JsonInventoryRepository` updates through separate service instances and confirm that exactly the available units are removed without same-process overselling.

Later steps should add focused unit tests for domain invariants and application use cases, plus web integration tests for relevant HTTP behavior. Tests should verify the assignment rules described in [business-rules.md](business-rules.md) and security-sensitive behavior described in [security.md](security.md) as those features are implemented.

## Commands

From the solution root:

```powershell
dotnet restore ApexPost.slnx
dotnet build ApexPost.slnx --no-restore
dotnet test ApexPost.slnx --no-build
```
