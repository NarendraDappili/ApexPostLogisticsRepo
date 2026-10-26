# ADR 0007: Complete inventory compensation after request cancellation

Status: Accepted.

## Context

Checkout removes inventory before persisting a transaction. Refund persists its status before restoring inventory. If the HTTP request is cancelled during either compensation path, using the request token can leave stock inconsistent with the transaction state.

## Decision

After checkout has changed inventory, rollback writes use `CancellationToken.None`. After refund status is persisted, inventory restoration also uses `CancellationToken.None`. The original request token still governs validation and the transaction persistence operation.

## Consequences

The current in-process compensation is allowed to finish after a client disconnect, reducing avoidable partial state. This does not provide crash recovery, cross-process coordination, multi-file atomicity, or a durable retry queue. Compensation failures remain surfaced for reconciliation.
