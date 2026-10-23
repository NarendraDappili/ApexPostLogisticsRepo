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

No domain entities, rules, transaction workflows, or persistence are implemented in Step 2. These requirements are documented here for later implementation and testing; they are not yet enforced by the template application.
