# Business rules and implementation status

## Enforced rules

| Rule | Enforcement |
| --- | --- |
| Stock never becomes negative | `BranchInventoryItem` and atomic `IInventoryRepository.UpdateAsync`; checkout and inventory tests cover underflow/concurrency. |
| Inactive staff cannot transact | `StaffMember.CanProcessTransactionsAt`, checkout workflow, cookie validator, and authentication tests. |
| Clerks use their assigned branch | Checkout validates the clerk/branch; MVC authorization and `SameBranchOnly` restrict HTTP access. |
| High-value money orders need verified same-context identity | `MoneyOrderWorkflowService` and checkout compare configured threshold, outcome, and transaction context. |
| Shipment service weight limit | `ShippingPriceCalculator` rejects a rate-limit exceedance before persistence. |
| Void/refund require assigned active Branch Manager and reason | MVC policy narrows HTTP access; Application workflow is the final rule authority. |
| Closed branches cannot transact | Shipping, money-order creation, and checkout reject closed branches. |

Money uses `decimal`. The repository does not invent a currency, production rounding policy, fee schedule, document-type policy, or dimensional surcharge; those require explicit future policy decisions.

## Synthetic seed data

`DemoDataSeeder` creates five open branches, ten counters, twenty active staff members across all four roles, thirty products, 150 inventory records, ten zones/60 rates, and forty historical transaction snapshots. It is deterministic and contains no password, API key, full identity-document number, or real personal data.

Historical transaction snapshots are seed artifacts, not a replacement for runtime `CounterTransaction` persistence. Runtime transactions use `counter-transactions.json`; reports query persisted runtime aggregates.

## Operational boundaries

JSON files do not provide a multi-file transaction. Checkout compensates inventory after a transaction-save failure, while refund records its status before attempting stock restoration and reports restoration failure for reconciliation. Locks protect same-process paths only; multiple processes sharing a data directory are unsupported.

## Deliberately incomplete or external concerns

- No external payment, identity-verification, notification, or shipping-carrier adapter.
- No production account provisioning, MFA, lockout, password lifecycle, or cross-process session revocation.
- No database/ORM, distributed lock, outbox, or reconciliation job.
- No automatic seed/reset CLI command; operators must explicitly control a local data directory.
