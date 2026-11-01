# ADR 0011: Write audit entries outside the request's unit of work

Status: Accepted. Supersedes the storage part of ADR 0005.

## Context

The MVC audit filter records every action, including failed ones. ADR 0005 stored those records in a JSON Lines file. Week 3 moves them into the `AuditEntries` table behind the same `IAuditStore` interface, and requires that an audit write never breaks the business transaction.

Each request has one scoped `ApexPostDbContext`, shared by the repositories and `EfUnitOfWork`. The audit filter writes its record after the action and its result have run, so the business commit (or its failure) has already happened when the audit record is written.

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

### The status code recorded is the one the client received

The Week 2 filter was an action filter. It guessed the status from the action's result type before the result ran, which was wrong in four cases:
- `LocalRedirect` (sign-in), `RedirectToPage`, `Forbid` and `Challenge` fell through to 200. A refused cross-branch attempt was audited as "200 Succeeded".
- Permanent and method-preserving redirects were all recorded as 302.
- A form re-rendered with 409 was recorded as 200.
- A business exception that `ProblemDetailsExceptionMiddleware` turns into 409/404/400 was recorded as 500.

It is now `AuditResourceFilter`, a **resource filter**. Resource filters wrap model binding, the action and the result execution, so:
- **When no exception escapes MVC,** the filter records `Response.StatusCode` after the result has run. That is the real status: the redirect's 301/302/307/308, the 403 from `Forbid`, the 409 page, the 400 Problem Details.
- **When an exception escapes MVC,** the filter records the status the middleware will send for it. It uses the same `ExceptionProblemMapping` (ADR 0018): 409/404/400 for mapped business errors, 499 for a client abort, otherwise 500.

The duration now includes result execution, such as view rendering, so it reflects the whole MVC request. Requests refused by authorization (antiforgery or policy failures) are rejected before MVC filters run and are not audited by this filter, as before. The regression test `Audit_records_the_status_the_client_received_for_redirects_refusals_and_mapped_errors` fails against the old filter.

## Consequences

- A business change that failed or was rolled back is audited as `Failed`, and the audit write cannot commit it by accident.
- An audit failure cannot undo a committed sale; it is logged with the controller and action so monitoring can pick it up.
- The audit is best-effort, not transactional with the business change. A crash between the business commit and the audit insert loses that one record. Where every committed change must be audited, an outbox written in the business transaction would be needed; that is not required here.
- Each audited request opens one extra short connection from the pool.
- Tests: `EfAuditStoreTests` (round trip, failed business save audited but not committed, failed audit leaves the committed change) and `An_audit_store_failure_is_logged_and_never_breaks_a_committed_sale`.
