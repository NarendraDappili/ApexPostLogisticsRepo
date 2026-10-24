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
- `InventoryService` coordinates branch/product stock changes through atomic repository updates. Adds and removals require positive quantities; a general adjustment must be non-zero. Domain inventory rejects any removal that would make stock negative.
- A low-stock notification is prepared only when stock crosses from above a configured non-negative threshold to at/below it. Notification delivery is an existing Application abstraction; no delivery implementation is introduced here.
- `ShippingWorkflowService` creates a shipment only when its origin branch exists and is open, its identifier is unused, and `ShippingPriceCalculator` finds exactly one configured rate for its type, service level, and destination zone.
- The selected rate supplies the fixed price and maximum weight. A shipment at the limit is accepted; one above it is rejected before persistence. The current shipment model requires strictly positive dimensions for letters and parcels alike; no letter defaults or further dimension limits are inferred.
- Money orders require a positive decimal amount, non-empty sender/recipient names, a non-negative fee, and a context identifier. Their total is amount plus fee without a new fee or rounding formula.
- The configured `IMoneyOrderPolicy.HighValueThreshold` is exclusive: an amount strictly above it requires a verified identity record in the same money-order context; an amount equal to it does not. Failed, pending, absent, or mismatched-context verification rejects a high-value order before persistence.
- Identity verification records retain only document type, result, context ID, and a masked document reference. Full document numbers are not retained by Domain or JSON persistence.
- Counter transactions contain one or more retail or service lines, a configured-policy tax amount, payment method, and `Open`, `Paid`, `Voided`, or `Refunded` status. Checkout produces a paid transaction only after open branch/counter and active, assigned Counter Clerk checks pass.
- Retail lines use the existing retail calculator. A service line carries a price produced by its owning service; checkout does not duplicate shipping, money-order, or other service pricing formulas. Existing money-order lines must match the transaction ID as their context and satisfy the high-value verification policy.
- Transaction state transitions are `Open → Paid`, `Open → Voided`, and `Paid → Refunded`. Void/refund require a non-empty reason and an active Branch Manager assigned to the transaction branch. Checkout removes retail stock only as it marks a transaction paid, so refunds restore retail stock while voiding an open basket does not.

### Demo seed data

- Step 8 seed data is synthetic and deterministic: it includes five open branches, active synthetic staff in every required role, products, non-negative inventory, ten valid zone codes with shipping-rate records, and forty historical retail records.
- Every historical record refers to a seeded branch, an active Counter Clerk assigned to that branch, and a seeded product. Its total equals its decimal unit price multiplied by its positive quantity; a corresponding non-negative inventory record exists.
- The current model has no transaction aggregate, tax policy, money-order threshold, or shipping-rate repository. Historical transaction and rate files are therefore Infrastructure seed snapshots for future consumers, not evidence of implemented transaction workflows.
- Seed data contains no customer identity-document field or reference. Names and identifiers are explicitly synthetic; no passwords, API keys, or credentials are seeded.

### Deferred to future steps

- `JsonShippingRateProvider` loads `shipping-rates.json` for runtime workflow use. Configuration must contain exactly one valid rate for a supported type/service/zone combination; missing or ambiguous rates are rejected by the pricing component.
- The high-value threshold and same-transaction linkage for money-order identity verification require transaction composition and configuration.
- JSON transaction operations span inventory and transaction files and are not atomic. Checkout compensates inventory if transaction persistence fails; process interruption, failed compensation, and multiple application processes require operational reconciliation.
- Branch closure must be coordinated with counter transaction creation in a later workflow; this step exposes branch state without adding a transaction process.
- Application now owns contracts that future workflows will use to retrieve and persist entities, identify the current staff member, write audits, and send notifications. The contracts do not themselves enforce business rules or persist data.
- JSON inventory atomicity is limited to repository instances in one application process. Multiple processes writing the same JSON data directory require external coordination and are unsupported.
- A production high-value threshold, supported document types, external identity-provider integration, fee schedule, currency, and rounding policies require explicit future configuration/policy decisions; no values are invented here.

### Requiring clarification

- Identifier formats and maximum lengths, supported currencies, rounding policy, dimensional surcharges or limits, and the high-value money-order threshold are not specified in the repository. Shipping rate values and service-level weight limits are caller-supplied configuration, not authoritative values in source code.
