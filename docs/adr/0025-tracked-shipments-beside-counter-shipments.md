# ADR 0025: Tracked shipments beside the counter's shipments

Status: Accepted (Week 4, Part B).

## Context

The tracking API (Part B) needs shipments with:
- a public tracking number (`APX-` and eight digits);
- a recipient (name, phone, address, city);
- a weight of 0.1 to 70 kg;
- a status that moves Created → PickedUp → InTransit → OutForDelivery → Delivered or Failed;
- a history of courier scans.

Week 3 already has a `Shipment`, but it is a different thing. A clerk accepts it at a branch counter, it is priced from the zone rates and billed by a counter transaction, and it has:
- an internal code, an origin branch, a destination zone, dimensions and a service level;
- **no** recipient, status or scans;
- a weight limit of **30 kg**: the counter's acceptance limit, enforced by the domain, a CHECK constraint and the rate table.

The brief also says: *"Do not modify the Week 3 data layer except to add new service methods."*

## Options

1. **Extend the Week 3 `Shipment`** with a tracking number, recipient, status and scans. This changes a table the counter, the reports and the stored procedures depend on. The 30 kg rule would have to become 70 kg for some shipments and stay 30 kg for others. Every Week 3 shipment would need a recipient and status it never had.
2. **A separate database or `DbContext` for the API.** This leaves Week 3 untouched, but does not *reuse* the Week 3 data layer as the brief requires. The API would also lose the audit trail, the unit of work and the error translation.
3. **A new aggregate in the same model**, added by a new migration that only creates tables.

## Decision

Option 3.
- **Domain:** `TrackedShipment` is a new aggregate in `ApexPost.Domain`, with `TrackingNumber`, `Recipient` (an owned value) and `TrackingStatusRules`. `TrackingScan` is a separate entity.
- **Database:** the new tables are `TrackedShipments` and `TrackingScans`. They come from new `IEntityTypeConfiguration` classes, which `ApexPostDbContext` picks up without being edited.
- **Shared infrastructure** comes from Week 3 unchanged: `EfUnitOfWork`, the audit interceptors, the `rowversion` concurrency pattern and the error types.
- **Status rules** live in the domain. The service checks them first, so it can throw a typed 409. `EvaluateScan` decides each uploaded scan:
  - the next status moves the shipment;
  - a repeat, or a late scan for a stage already passed, is kept as history;
  - anything else is refused with a reason.
- **Scans** reference the shipment by its tracking number, with `DeleteBehavior.Restrict`. The device's scan ID is a unique key, which makes uploads idempotent.

## Consequences

- **Week 3 is unchanged.** The migration `AddShipmentTracking` only adds tables; its `Down` drops them. No Week 3 table, procedure, index or test fixture changed. The only edited Week 3 test checks the name of the newest migration.
- **Counter and tracking shipments are not linked.** A parcel accepted at the counter does not automatically get a tracking number. Linking them later is an additive change: an optional `ShipmentCode` on `TrackedShipments`.
- **The audit trail covers the API.** Every API change is recorded in `DataChanges` with the request's trace ID.
- **Least privilege.** The API has its own role, `apexpost_api`. It can touch only the two tracking tables and append to `DataChanges`, and a test runs real work under it.
- **Two limits on the same idea.** The counter limit (30 kg) and the tracking limit (70 kg) are separate constants for separate business rules.
