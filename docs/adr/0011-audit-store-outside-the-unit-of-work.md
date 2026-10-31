# ADR 0011: Write audit entries outside the request's unit of work

Status: Accepted. Supersedes the storage part of ADR 0005.

## Context

The MVC audit filter records every action, including failed ones. ADR 0005 stored those records in a JSON Lines file. Week 3 moves them into the `AuditEntries` table behind the same `IAuditStore` interface, and requires that an audit write never breaks the business transaction.

Each request has one scoped `ApexPostDbContext`, shared by the repositories and `EfUnitOfWork`. The audit filter runs after the action, so the business commit (or its failure) has already happened when the audit record is written.

## Options considered

- **Share the request's unit of work.** The audit row would be added to the request's context and saved with it. After a failed save, that context still tracks the rejected changes (for example a duplicate staff email), so the audit `SaveChanges` would try to save them again and fail too. The failed action would then not be audited at all. Saving the audit row in the business commit would also mean an audit failure rolls back a sale.
- **Write audit rows separately.** Every append uses its own short-lived context, so its own connection and transaction.

## Decision

`EfAuditStore` creates a new `ApexPostDbContext` for each append from the request's `DbContextOptions`, adds one `AuditEntryEntity` and saves it. It never uses the request's context or unit of work.

- The options are the request's, so the audit-column interceptor stamps `CreatedBy` with the signed-in staff member, like every other table.
- Once started, the audit save uses `CancellationToken.None`: the audited action has already happened, so a client disconnect must not drop its record.
- `AuditEntries` has no foreign keys. It must record failed sign-ins for unknown staff IDs and must outlive later changes to the staff and branch rows it mentions.
- The audit filter keeps its Week 2 behaviour: an append failure is logged and does not change the action's result.
- The store offers append and read only, no update or delete.

## Consequences

- A business change that failed or was rolled back is audited as `Failed`, and the audit write cannot commit it by accident.
- An audit failure cannot undo a committed sale; it is logged with the controller and action so monitoring can pick it up.
- The audit is best-effort, not transactional with the business change. A crash between the business commit and the audit insert loses that one record. Where every committed change must be audited, an outbox written in the business transaction would be needed; that is not required here.
- Each audited request opens one extra short connection from the pool.
- Tests: `EfAuditStoreTests` (round trip, failed business save audited but not committed, failed audit leaves the committed change) and `An_audit_store_failure_is_logged_and_never_breaks_a_committed_sale`.
