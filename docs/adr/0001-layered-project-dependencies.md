# ADR 0001: Preserve inward project dependencies

Status: Accepted — documented at Step 20.

## Context

Business rules must remain independent of MVC, JSON, and HTTP. The solution already separates Domain, Application, Infrastructure, and Web projects.

## Options considered

- Keep Domain independent and place contracts in Application.
- Let Domain reference Infrastructure or Web for persistence/HTTP conveniences.

## Decision

Keep the inward dependency flow: Domain has no solution-project references; Application depends on Domain; Infrastructure adapts Application/Domain; Web composes Application and Infrastructure.

## Consequences

Domain tests stay fast and infrastructure-independent. Adapters and DI wiring require more explicit composition, which is intentional.
