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

## Implementation status

### Enforced by Domain

- Branch, staff, product, and zone identifiers must be non-empty after trimming and are normalized to uppercase invariant text. No code format or maximum length has been specified, so none is enforced.
- Monetary amounts are decimal, non-negative, and not rounded by the value object. Currency and rounding policy remain planned.
- Parcel weights are strictly positive kilograms; the configured service-level maximum is not yet implemented.
- Parcel length, width, and height are strictly positive centimetre values.
- Branches and counters have explicit open/closed state. A closed branch reports that it cannot accept transactions.
- Staff members can be activated or deactivated. Inactive staff cannot process transactions, and active Counter Clerks can process transactions only at their assigned branch.
- Branch inventory rejects negative starting stock and cannot be reduced below zero.
- Money orders retain only an identity-verification outcome; the model has no identity-document field.

### Deferred to future steps

- The configured shipment weight maximum is not yet available, so shipments retain their validated measurements but are not evaluated against a service limit.
- The high-value threshold and same-transaction linkage for money-order identity verification require transaction composition and configuration.
- Counter transaction line items, pricing, tax, voids, refunds, Branch Manager authorization, and reason capture require later transaction workflows.
- Branch closure must be coordinated with counter transaction creation in a later workflow; this step exposes branch state without adding a transaction process.
- Application now owns contracts that future workflows will use to retrieve and persist entities, identify the current staff member, write audits, and send notifications. The contracts do not themselves enforce business rules or persist data.

### Requiring clarification

- Identifier formats and maximum lengths, supported currencies, rounding policy, service weight limits, and the high-value money-order threshold are not specified in the repository and remain intentionally unconstrained here.
