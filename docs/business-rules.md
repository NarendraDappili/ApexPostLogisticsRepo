# Business rules and implementation status

## Enforced rules

| Rule | Enforcement |
| --- | --- |
| Stock never becomes negative | `BranchInventoryItem` and atomic `IInventoryRepository.UpdateAsync`; checkout and inventory tests cover underflow/concurrency. |
| Inactive staff cannot transact | `StaffMember.CanProcessTransactionsAt`, checkout workflow, cookie validator, and authentication tests. |
| Clerks use their assigned branch | Checkout validates the clerk/branch; MVC authorization and `SameBranchOnly` restrict HTTP access. |
| High-value money orders need verified same-context identity | Orders are issued as Pending; a separate verification step records the verifier's outcome; checkout refuses a high-value order unless it is Verified for the same transaction. |
| Prices, fees and verification results come from the server | Basket requests carry identifiers only; retail, shipment and money-order fee prices are resolved by the server (ADR 0009). |
| Shipment service weight limit | `ShippingPriceCalculator` rejects a rate-limit exceedance before persistence. |
| Void/refund require assigned active Branch Manager and reason | MVC policy narrows HTTP access; Application workflow is the final rule authority. |
| Closed branches cannot transact | Shipping, money-order creation, and checkout reject closed branches. |

Money uses `decimal` and is rounded to cents away from zero (`Money.RoundedToCents`). The money-order fee is a configured flat fee plus a percentage. The repository does not invent a currency or dimensional surcharge.

## Synthetic seed data

`DemoDataSeeder` creates five open branches, ten counters, twenty active staff members across all four roles, thirty products, 150 inventory records, ten zones/60 rates, and forty historical counter transactions with their shipments and money orders, dated over the four days before seeding. In Development it runs automatically on an empty data folder and stores PBKDF2 hashes of the demo password; it contains no API key, full identity-document number, or real personal data.

Historical transaction snapshots are seed artifacts, not a replacement for runtime `CounterTransaction` persistence. Runtime transactions use `counter-transactions.json`; reports query persisted runtime aggregates.

## Operational boundaries

JSON files do not provide a multi-file transaction. Checkout compensates inventory after a transaction-save failure, while refund records its status before attempting stock restoration and reports restoration failure for reconciliation. Locks protect same-process paths only; multiple processes sharing a data directory are unsupported.

The repository tracks synthetic JSON starting snapshots in `src/ApexPost.Web/App_Data/`, including audit and runtime record files. Running the application can modify them. Review these file changes before committing and never commit real personal or operational data. The generated `data-protection-keys/` directory in `App_Data` is excluded from Git and must remain local.

## Deliberately incomplete or external concerns

- No external payment, identity-verification, notification, or shipping-carrier adapter.
- No production account provisioning, MFA, lockout, password lifecycle, or cross-process session revocation.
- No database/ORM, distributed lock, outbox, or reconciliation job.
- No automatic seed/reset CLI command; operators must explicitly control a local data directory.
