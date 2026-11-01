# Business rules and where they are enforced

Each critical rule is enforced in the domain or application layer, and, where the database can express it, again in SQL Server. A rule therefore also holds for scripts and tools that bypass the application.

| Rule | Application side | Database side |
|---|---|---|
| Stock never becomes negative | `BranchInventoryItem.RemoveStock`/`Adjust`; a stale stock read is refused with 409 (rowversion) | `CK_BranchStock_Quantity` (`>= 0`); `usp_AdjustStock` checks inside its `UPDATE` |
| Two clerks cannot both sell the last unit | One `SaveChangesAsync` per checkout; `rowversion` on `BranchStock` | The `UPDATE … WHERE RowVersion = @original` affects no row for the second clerk |
| Every stock correction has a history row with a reason and a staff member | `StockAdjustment` (domain); `StockAdjustmentService` | `usp_AdjustStock` writes stock and history in one transaction; the app role has no direct access to `StockAdjustments` |
| Inactive staff cannot transact or sign in | `StaffMember.CanProcessTransactionsAt`; the cookie is re-validated against `Staff` on every request | — |
| Clerks work at their assigned branch | Checkout validates clerk and branch; `SameBranchOnly` policy; controllers compare the submitted branch with the cookie | — |
| High-value money orders need a verified identity for the same transaction | Orders are issued Pending; the verifier decides the outcome; checkout refuses unverified high-value orders | `CK_MoneyOrders_Amount` (`> 0`), `CK_MoneyOrders_Fee` (`>= 0`), `CK_IdentityVerifications_Outcome` |
| Identity documents are stored masked only | `MaskedIdentityDocumentReference`; `MoneyOrderMapper` rejects unmasked values | `varchar(8)` column and `CK_IdentityVerifications_Masked` (`LIKE '****%'`) |
| Prices, fees and verification results come from the server | The basket carries identifiers only; retail, shipping and money-order prices are resolved by services (ADR 0009) | — |
| A parcel weighs more than 0 and at most 30 kg, and within its rate's band | `ParcelWeight.MaximumKilograms`; `ShippingPriceCalculator` | `CK_Shipments_WeightKg`, `CK_ShippingRates_MaximumWeightKg` |
| A void or refund needs an assigned, active Branch Manager and a reason | `BranchManagerOnly` policy; the workflow checks the manager; `CounterTransaction.Void`/`Refund` | `CK_CounterTransactions_Reversal`: a voided or refunded sale must name the manager and the reason, and an open or paid sale must not |
| Only open or paid sales can be voided; only paid ones refunded | `CounterTransaction` state machine | `CK_CounterTransactions_Status` (allowed values) |
| A reversal returns the stock with the status change | Status and stock are committed in one `SaveChangesAsync` | — |
| Closed branches and counters cannot transact | Shipping, money-order and checkout workflows | — |
| Business codes are unique (branch, SKU, staff email, transaction, shipment, money order) | Duplicate checks in the workflows; `DuplicateKeyException` becomes 409 | Unique constraints and indexes |
| Price revisions stay within −50% … +100% and change one category | `PriceRevisionService` | `CK_Products_UnitPrice` (`>= 0`) |

Money is `decimal` (`decimal(18,2)` in SQL Server), rounded to cents away from zero by `Money.RoundedToCents`. The money-order fee is a configured flat fee plus a percentage. No currency or dimensional surcharge is invented.

## Seed data

`DemoDataSeeder` fills an empty, fully migrated `ApexPostDb` in Development, in one transaction. It writes:
- 5 open branches with 2 counters each;
- 20 active staff across the four roles, each with a PBKDF2 hash of the demo password;
- 30 products in 3 categories, stocked at every branch (the first three products start at low stock);
- 10 zones with 60 rates;
- 240 sales over the last 60 days, with their shipments, money orders and identity verifications. A few sales are voided or refunded, and each branch has one refused money order for the auditor view.

Everything is fictional and only masked document references are stored. The reference data the schema needs (the four roles and the `GENERAL` category) is seeded by the migrations instead.

## Out of scope

- External payment, identity-verification, notification and shipping-carrier services. The verifier and the courier lookup are simulated, and notifications are log entries.
- Production account provisioning, MFA, account lockout and password rotation.
- Guaranteed audit. An audit write failure is logged and never undoes a sale (ADR 0011); an outbox would be needed to guarantee every record.
