# ADR 0009: Price every basket line on the server and verify identity in a separate step

Status: Accepted.

## Context

The review found that several amounts and checks came from the browser: a service line's description and price (an "Express parcel" at 0.01 was accepted), the money-order fee, and the identity-verification outcome, which the clerk picked from a list. Shipments were created separately and never billed in a basket.

## Options considered

- Keep free-form service lines and validate the typed price against a range.
- Accept only identifiers in the basket and resolve every price on the server from the record that owns it.

## Decision

The checkout request carries identifiers only:

- **Retail lines** (several per basket) are priced from the catalogue.
- **Shipments** are created with the ID of the transaction that will bill them. Checkout loads each shipment, checks it belongs to this branch and this transaction, and prices it from the configured rate table. Because a transaction ID is unique, a shipment can be billed once only.
- **Money orders** carry a fee calculated by `IMoneyOrderPolicy` from configuration (`FlatFee` plus `FeeRate` of the amount, rounded to cents by the `Money.RoundedToCents` extension member).
- **Identity verification** is its own step after a money order is issued. The clerk records the document type and number; `IIdentityVerifier` decides `Verified` or `Failed`; only the type and a masked reference are stored. A high-value order can be issued while pending, but checkout refuses to complete it until a successful verification exists for the same transaction context.

## Consequences

- No price, fee or verification outcome is accepted from a form. Tampering with the request can only reference records that the server prices itself.
- The rule "a high-value money order cannot be completed without verification in the same transaction" is enforced at completion (checkout), which is where money changes hands.
- Identity verification is simulated (`SimulatedIdentityVerifier`); a real provider replaces it behind the same interface.
