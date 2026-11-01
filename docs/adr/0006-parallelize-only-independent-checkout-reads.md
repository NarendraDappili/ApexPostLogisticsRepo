# ADR 0006: Parallelize only independent checkout validation reads

Status: Superseded by ADR 0017. One DbContext per request must not run queries in parallel, so the checkout reads are sequential. `Task.WhenAll` is used again for the independent reports, each with its own context (ADR 0021).

## Context

Checkout validation needs branch, counter, staff, and duplicate-transaction reads. They query independent aggregates/files, while inventory updates and transaction persistence have ordering and compensation requirements.

## Options considered

- Await every validation read sequentially.
- Use `Task.WhenAll` for all checkout work.
- Use `Task.WhenAll` only for independent validation reads.

## Decision

Start the four validation reads together and await them with `Task.WhenAll`; retain per-branch locking and ordered stock/remove-save-compensate behavior for mutation.

## Consequences

The validation path avoids unnecessary sequential waiting without introducing concurrent writes or promising a coherent cross-file snapshot. No numeric speedup is claimed because no representative benchmark exists.
