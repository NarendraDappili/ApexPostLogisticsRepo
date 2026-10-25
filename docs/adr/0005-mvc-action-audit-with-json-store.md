# ADR 0005: Audit at the MVC action boundary with a JSON store

Status: Accepted — documented at Step 20.

## Context

The audit requirement needs controller/action identity, model-validation outcome, action result, authenticated staff context, and correlation ID without logging sensitive input.

## Options considered

- HTTP middleware audit.
- Global MVC action filter backed by an Application `IAuditStore` and Infrastructure JSON implementation.
- Controller-by-controller audit code.

## Decision

Use global `AuditActionFilter` and `IAuditStore`/`JsonAuditStore`. The filter records only allow-listed route identifiers and never serializes bodies, headers, models, or action arguments.

## Consequences

MVC action metadata is available consistently, and audit I/O remains asynchronous. The JSON API is append-only by contract but not tamper-proof, WORM, or cross-process safe.
