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
- Retail pricing multiplies a product's current decimal unit price by a positive quantity without rounding.
- Shipping pricing selects exactly one configured fixed charge using shipment type, service level, and destination zone. It rejects shipments whose weight is above that rate's configured maximum; a weight equal to the maximum is accepted.

### Deferred to future steps

- JSON configuration loading and persistence for shipping rates are not implemented; callers currently supply configured rate data directly to the calculator.
- The high-value threshold and same-transaction linkage for money-order identity verification require transaction composition and configuration.
- Counter transaction line items, pricing, tax, voids, refunds, Branch Manager authorization, and reason capture require later transaction workflows.
- Branch closure must be coordinated with counter transaction creation in a later workflow; this step exposes branch state without adding a transaction process.
- Application now owns contracts that future workflows will use to retrieve and persist entities, identify the current staff member, write audits, and send notifications. The contracts do not themselves enforce business rules or persist data.

### Requiring clarification

- Identifier formats and maximum lengths, supported currencies, rounding policy, dimensional surcharges or limits, and the high-value money-order threshold are not specified in the repository. Shipping rate values and service-level weight limits are caller-supplied configuration, not authoritative values in source code.
