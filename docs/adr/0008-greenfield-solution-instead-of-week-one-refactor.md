# ADR 0008: Build ApexPost as a new solution instead of refactoring the previous project

Status: Accepted.

## Context

The Week 2 brief replaced the previous business domain with the ApexPost Counter Platform and asked teams to refactor their earlier solution, keeping its architecture if it still fit and explaining any redesign. The project's working instructions instead said to build ApexPost from scratch. The review asked for that decision to be recorded.

## Options considered

- Refactor the earlier solution in place, renaming and reshaping its entities and services into the postal, retail and financial-services domain.
- Start a new solution and carry forward only the architectural approach (layered projects, repository abstractions over JSON, dependency injection), not the code.

## Decision

Start a new .NET 10 solution with separate Domain, Application, Infrastructure and Web projects plus one test project per layer. The layered architecture and the "JSON behind repository interfaces" persistence approach were kept, because they still fit; the domain model, services and UI were written new.

The reasons were:

- Almost none of the earlier domain survived the change. ApexPost needs branches with counters, a mixed basket of goods and services, a pricing strategy, stock per branch, money orders with identity verification and manager-authorised reversals. Renaming the earlier entities would have left behaviour and invariants that no longer applied.
- This week also adds an MVC front end, middleware and filters on top of the services. Designing the Domain and Application contracts for that from the start was cheaper than reshaping existing ones while keeping them working.
- A clean history of small feature branches (one per step) made each part of the new design reviewable on its own.

## Consequences

- The architecture (inward-pointing project references, repository interfaces in Application, JSON implementations in Infrastructure, `Program.cs` as composition root only) is unchanged from the earlier approach and is documented in ADR 0001 and ADR 0002.
- No code-level migration path from the earlier project exists; anything reused later must be ported deliberately.
- The trade-off is that the brief's "refactor" step is not visible as a diff; this ADR records why.
