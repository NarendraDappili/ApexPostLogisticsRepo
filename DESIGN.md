# ApexPost Logistics Design

## Current architecture

The solution is structured as a layered application. The Domain layer now provides validated value objects; domain entities, use cases, persistence, and application-specific MVC workflows are not yet implemented.

| Project | Responsibility | Current state |
| --- | --- | --- |
| `ApexPost.Domain` | Business entities, value objects, and invariants. | Value objects implemented; entities and broader invariants planned. |
| `ApexPost.Application` | Use cases, service contracts, and repository abstractions. | Project shell only. |
| `ApexPost.Infrastructure` | Technical adapters, including planned JSON persistence. | Project shell only. |
| `ApexPost.Web` | ASP.NET Core MVC UI and composition root. | Minimal MVC template; no application-specific registration or workflow. |
| `ApexPost.Domain.Tests` | Unit tests for Domain. | Value-object unit tests implemented. |
| `ApexPost.Application.Tests` | Unit tests for Application. | Template test only. |
| `ApexPost.Web.IntegrationTests` | Integration tests for Web. | Template test only. |

## Verified project dependency flow

```mermaid
flowchart RL
    Web[ApexPost.Web] --> Application[ApexPost.Application]
    Web --> Infrastructure[ApexPost.Infrastructure]
    Infrastructure --> Application
    Infrastructure --> Domain[ApexPost.Domain]
    Application --> Domain
    DomainTests[ApexPost.Domain.Tests] --> Domain
    ApplicationTests[ApexPost.Application.Tests] --> Application
    WebIntegrationTests[ApexPost.Web.IntegrationTests] --> Web
```

`ApexPost.Domain` deliberately has no solution-project references. This keeps business rules independent of HTTP, MVC, JSON, and persistence details. The dependency direction is inward: Application depends on Domain, Infrastructure adapts Application and Domain, and Web uses Application and Infrastructure at the outer edge.

## Composition root and dependency injection

`src/ApexPost.Web/Program.cs` is the composition root. It currently contains only template application setup. In a later step, it will register Application services and Infrastructure implementations with built-in `Microsoft.Extensions.DependencyInjection`, then configure the HTTP pipeline. Business logic will not be placed there.

## Domain value objects

The Domain layer contains immutable sealed records, so equality is value-based and instances cannot be modified after construction. `BranchCode`, `StaffId`, `ProductId`, and `ZoneCode` reject null, empty, and whitespace-only input, then trim and normalize codes to uppercase invariant text. The assignment does not provide code-pattern or length limits, so none are invented here.

`Money` retains its input as a `decimal` and accepts zero or positive amounts without rounding; currency and rounding policies are planned with future pricing and tax work. `ParcelWeight` represents a strictly positive kilogram value. `ParcelDimensions` represents strictly positive length, width, and height values in centimetres. Shipment weight limits remain a planned configurable business rule rather than a value-object maximum.

```mermaid
flowchart TB
    Domain[ApexPost.Domain<br/>implemented] --> Codes[BranchCode, StaffId, ProductId, ZoneCode<br/>implemented]
    Domain --> Money[Money<br/>implemented]
    Domain --> Weight[ParcelWeight<br/>implemented]
    Domain --> Dimensions[ParcelDimensions<br/>implemented]
    PlannedEntities[Future domain entities<br/>planned] -. use .-> Codes
    PlannedEntities -. use .-> Money
    PlannedEntities -. use .-> Weight
    PlannedEntities -. use .-> Dimensions
```

```mermaid
flowchart LR
    Input[Raw constructor input] --> Validate[Validate and normalize where applicable]
    Validate --> Valid{Input valid?}
    Valid -->|Yes| ValueObject[Immutable value object created]
    Valid -->|No| Error[Argument exception]
```

Future domain entities will compose these types to retain their validation at creation boundaries; those entities are not implemented in this step.

## Planned HTTP request flow

The following is an intended design, not current functionality. Components marked **planned** do not yet exist in the implementation.

```mermaid
flowchart LR
    Client[Client] --> Middleware[ASP.NET Core middleware<br/>planned pipeline configuration]
    Middleware --> Controller[MVC controllers / Razor Views<br/>planned application workflows]
    Controller --> AppService[Application services<br/>planned]
    AppService --> Repository[Repository abstractions<br/>planned]
    Repository --> Infrastructure[Infrastructure JSON implementations<br/>planned]
    Infrastructure --> Files[(JSON files<br/>planned)]
```

MVC, Razor Views, and the template HTTP pipeline are present through the ASP.NET Core template. The depicted application services, repository abstractions, JSON implementations, and JSON files remain planned.

## Test structure

Each test project references only the layer it tests: Domain Tests references Domain, Application Tests references Application, and Web Integration Tests references Web. The projects use xUnit; Web Integration Tests also references `Microsoft.AspNetCore.Mvc.Testing` for its planned web-hosting setup. Application-specific tests and use of that hosting setup are planned for later steps.

## Planned architectural components

Later steps may add domain entities and invariants, application service and repository-abstraction contracts, JSON persistence adapters, dependency-injection registrations, authentication and authorization, and MVC workflows. None of those components are implemented by this configuration step.
