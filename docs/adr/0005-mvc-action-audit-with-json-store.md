# ADR 0005: Audit at the MVC action boundary with an append-only JSON Lines store

Status: Partly superseded. The JSON Lines storage is replaced by the AuditEntries table (ADR 0011); the filter became a resource filter that records the status the client received (ADR 0011). The safe-metadata rules below still apply.

## Context

The audit requirement needs controller/action identity, model-validation outcome, action result, authenticated staff context, and correlation ID without logging sensitive input.

The first implementation stored the log as one JSON array and rewrote the whole file on every audited action. At 405 KB the review noted that every action became slower as the log grew.

## Options considered

- HTTP middleware audit.
- Global MVC action filter backed by an Application `IAuditStore` and Infrastructure JSON implementation.
- Controller-by-controller audit code.

## Decision

Use global `AuditActionFilter` and `IAuditStore`/`JsonAuditStore`. The filter records only allow-listed route identifiers and never serializes bodies, headers, models, or action arguments.

`JsonAuditStore` writes JSON Lines (`audit-records.jsonl`): each record is one line appended to the end of the file under a same-process lock. The file is never read or rewritten when appending, so the cost of an audited action stays constant as the log grows. Once a write starts it is not cancelled, so a disconnecting client cannot leave a partial line.

## Consequences

MVC action metadata is available consistently, audit I/O remains asynchronous, and append cost no longer depends on log size. The store is append-only by contract but not tamper-proof, WORM, or cross-process safe; a crash during a write could leave one truncated final line, which `ReadAsync` reports as malformed JSON.
