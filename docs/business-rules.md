# Business rules

## Assignment requirements

The assignment requires support for branches and counters, staff and roles, product catalogue and branch inventory, shipping, money orders, identity verification, mixed retail/service transactions, pricing and tax, voids and refunds, reports, and simulated notifications.

The required roles are Counter Clerk, Branch Manager, Auditor, and Administrator. Required invariants include:

- Stock must not become negative.
- Inactive staff cannot process transactions.
- Clerks may transact only at their assigned branch.
- High-value money orders require successful identity verification in the same transaction.
- Shipments over the configured service-level weight limit are rejected.
- Voids and refunds require Branch Manager authorization and a reason.
- Closed branches cannot accept transactions.

## Verified implementation

Step 3 implements value-object constraints only; it does not implement entities, transaction workflows, or persistence. The following rules are now enforced:

- Branch, staff, product, and zone identifiers must be non-empty after trimming and are normalized to uppercase invariant text. No code format or maximum length has been specified, so none is enforced.
- Monetary amounts are decimal, non-negative, and not rounded by the value object. Currency and rounding policy remain planned.
- Parcel weights are strictly positive kilograms; the configured service-level maximum is not yet implemented.
- Parcel length, width, and height are strictly positive centimetre values.

The assignment-level invariants listed above remain planned for future entities and workflows.
