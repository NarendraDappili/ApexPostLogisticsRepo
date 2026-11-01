# ADR 0002: Use JSON persistence with same-process locking

Status: Superseded by ADR 0013 (schema), ADR 0015 (repositories and unit of work) and ADR 0017 (concurrency) in Week 3, when SQL Server replaced the JSON files.

## Context

The assignment requires JSON persistence and no ORM/database. Concurrent read-modify-write operations can otherwise corrupt data or oversell inventory.

## Options considered

- JSON files with a path-keyed `SemaphoreSlim` and complete temporary-file replacement.
- A database/ORM.
- Uncoordinated direct file writes.

## Decision

Use generic `JsonFileStore<TDocument>` with asynchronous streams, a process-wide path-keyed `SemaphoreSlim`, and same-directory temporary-file replacement.

## Consequences

Same-process writes are serialized and cancellation is honored. Multiple processes, filesystem durability, and multi-file atomicity are not guaranteed; reconciliation may be needed. The repository includes synthetic JSON starting snapshots under `src/ApexPost.Web/App_Data/`; runtime mutations can change tracked files and must be reviewed before committing. Real personal or production operational data must not be committed. The generated `data-protection-keys/` subdirectory is excluded from Git and remains local.
