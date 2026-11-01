# ADR 0016: Map domain entities directly, with one separate persistence model for money orders

Status: Accepted.

## Context

The domain entities are rich:
- private setters;
- constructors that validate;
- behaviour such as `BranchInventoryItem.RemoveStock`, `CounterTransaction.Void`, `MoneyOrder.RecordIdentityVerification`;
- value objects for every code and amount.

EF Core can map such entities without changing them. The alternative is a separate persistence class plus a mapper per aggregate. The brief asks for at least one separate persistence model, justified, and for shadow properties for database-only columns.

## Decision

### Default: map the domain entity directly with the Fluent API

`Branch`, `Counter`, `StaffMember`, `Product`, `ProductCategory`, `BranchInventoryItem`, `Shipment`, `CounterTransaction` and `StockAdjustment` are mapped directly. Their `IEntityTypeConfiguration` classes:

- **keep database-only columns as shadow properties:** the surrogate keys, `RowVersion`, and the audit columns `CreatedAtUtc`, `CreatedBy` and `ModifiedAtUtc`. The domain never sees them.
- **convert value objects** with value converters: codes to `varchar`, `Money` to `decimal(18,2)`, `Sku`, `ParcelWeight`, and `DateTimeOffset` to UTC `datetime2(3)`.
- **use owned types** for values without identity: `ParcelDimensions` in the shipment row; `TransactionLines` and `Payments` in their own tables, owned by the sale.
- **map `Product.UnitPrice` as a complex property** (same column), so the set-based price revision can update it with `ExecuteUpdateAsync`.
- **use private constructors.** EF Core materialises entities through private constructors added for it, so the public constructors keep their validation. This was the only change made to the domain for persistence. It changes no behaviour, and the Week 2 domain tests pass unchanged.

The mapping is cheap for these entities because their stored shape matches the domain shape. A mapper would only copy every field twice.

### Exception: money orders have their own persistence entities

`MoneyOrder` is stored through `MoneyOrderEntity` and `IdentityVerificationEntity`, translated by `MoneyOrderMapper`. The reasons:

- **Masked storage is a security rule, and the mapper enforces it in both directions.**
  - Writing stores only `MaskedIdentityDocumentReference.Value`.
  - Reading rebuilds the aggregate through its constructor and `RecordIdentityVerification`, so stored rows are re-validated.
  - A stored reference that is not masked is rejected by `MaskedIdentityDocumentReference.FromMaskedValue` instead of reaching the domain.
  - The `varchar(8)` column and the `CHECK … LIKE '****%'` constraint repeat the rule in the schema (ADR 0013).
- **The shapes differ.**
  - In the domain, the verification is a value recorded on the order, together with the transaction context.
  - In the database it is a separate row with its own key and a unique FK to the order, so auditors can query verifications on their own (report W4).
  - Mapping that difference directly would bend the domain towards the table.
- **Updates are explicit.** Only the verification can change after issue, and `MoneyOrderMapper.Apply` copies exactly that onto the tracked entity. Amount, fee and parties are never written back.

`EfMoneyOrderRepository` keeps the loaded aggregate and its entity together for the request, so `Update` applies the change to the tracked row.

## Consequences

- **The domain stays free of persistence types.** It references nothing; there are no EF attributes, public setters or navigation properties added for EF.
- **Direct mapping keeps most of the persistence code small.** In exchange, the configurations must state explicitly what the conventions would otherwise guess.
- **Masking cannot be bypassed.** A document number can only reach the database as a masked reference, and a corrupted row fails loudly on read. The mapper and the extra entities are the cost, accepted for the one aggregate that carries sensitive data.
- **Tests:**
  - `ApexPostModelTests.Money_order_mapper_round_trips_the_aggregate_and_rejects_unmasked_storage`;
  - `Identity_document_column_can_hold_only_a_masked_reference`;
  - the SQL repository mapping tests.
