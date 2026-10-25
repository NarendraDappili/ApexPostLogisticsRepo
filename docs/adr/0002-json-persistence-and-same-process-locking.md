# ADR 0002: Use JSON persistence with same-process locking

Status: Accepted — documented at Step 20.

## Context

The assignment requires JSON persistence and no ORM/database. Concurrent read-modify-write operations can otherwise corrupt data or oversell inventory.

## Options considered

- JSON files with a path-keyed `SemaphoreSlim` and complete temporary-file replacement.
- A database/ORM.
- Uncoordinated direct file writes.

## Decision

Use generic `JsonFileStore<TDocument>` with asynchronous streams, a process-wide path-keyed `SemaphoreSlim`, and same-directory temporary-file replacement.

## Consequences

Same-process writes are serialized and cancellation is honored. Multiple processes, filesystem durability, and multi-file atomicity are not guaranteed; reconciliation may be needed.
