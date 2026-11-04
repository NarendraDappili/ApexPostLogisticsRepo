# ADR 0024: A complete audit trail, and an Audit log screen by role

Status: Accepted. Extends ADR 0011; the request audit it describes is unchanged.

## Context

After the Week 3 evaluation the trainer pointed out that "auditing is happening but not logging anywhere". That was fair. `AuditEntries` recorded one row per MVC request (ADR 0011), but:
- **Nobody could read it.** No screen showed it, and no role could look at it without SQL access.
- **It did not say what changed.** A row said that `CounterTransactions/New` succeeded, but not which stock went down, which price changed or which staff member was deactivated.
- **Sign-ins and refusals left no trace of their own.** A failed sign-in or a 403 appeared only as a request row, without the reason. A rate-limited request, a refused API key or an invalidated cookie never reached MVC, so it was not recorded at all.
- **Nothing reached the application log.** The audit rows were only in SQL Server, so log monitoring could not see them.

The constraints were the same as before:
- Never store or log passwords, document numbers, API keys or query strings.
- Keep the existing core intact: no change to the domain, the services, the request audit, or the existing tests.
- No new package.

## Options

1. **One wide table.** Add an event-type column and a free-text payload to `AuditEntries`. This is the least schema change, but it mixes three kinds of record with different columns, retention and readers into nullable columns and JSON. It also breaks ADR 0011's simple row shape.
2. **SQL Server features.** Change Data Capture, temporal tables or SQL Server Audit. They record data changes reliably, but the edition and the DBA must support them. They do not know the signed-in staff member or the correlation ID, and they cannot record a refused sign-in.
3. **Three append-only tables written by the application, one per kind of record, plus `ILogger`.**

## Decision

Option 3.

| Record | Table | Written by | Same transaction as the change? |
|---|---|---|---|
| Every MVC request | `AuditEntries` (unchanged) | `AuditResourceFilter` → `EfAuditStore`, own context (ADR 0011) | No: best-effort, after the result |
| Every row inserted, updated or deleted | `DataChanges` | `DataChangeAuditInterceptor`, and two decorators for the set-based writes | **Yes** |
| Sign-in, sign-out and refusals | `SecurityEvents` | `SecurityEventRecorder` → `EfSecurityEventStore`, own context | No: best-effort |

### Data changes: old and new values, in the business transaction

- **How it is captured.** `DataChangeAuditInterceptor` runs in `SavingChanges`. It reads the change tracker and adds one `DataChanges` row per changed table row to the **same** `SaveChanges`. The history therefore commits or rolls back with the change it describes. A failed save leaves no history, and a retried save does not double it. This is the outbox-style audit that ADR 0011 named as missing, for data changes.
- **What a row holds:**
  - the table;
  - the business key (`TransactionCode=TX-1`, `BranchCode=BR-NORTH, ProductCode=PRD-001`), never a surrogate identity;
  - the operation;
  - who made the change, at which branch, and the request's correlation ID;
  - a JSON list of `{ column, old, new }` holding only the columns that changed.
  Owned types stored in the owner's row, such as the parcel dimensions, are part of the owner's change. A child with only an identity key, such as a sale's line or payment, is named after its parent (`TransactionCode=TX-1 / TransactionLines`).
- **What is left out or hidden:**
  - `PasswordHash` is stored as `(hidden)`, so the history shows that a password changed, never its hash;
  - the audit columns and `RowVersion` change on every save and say nothing, so they are left out;
  - identity documents are already masked in the domain, so only the masked reference can appear.
- **Writes that bypass `SaveChanges` are recorded too.** The price revision is one set-based `UPDATE`, and stock adjustment runs `usp_AdjustStock`. `ICatalogueBulkUpdates` and `IStockAdjustments` are wrapped by `AuditedCatalogueBulkUpdates` and `AuditedStockAdjustments`.
  - **Price revision:** the decorator reads the category's prices with `UPDLOCK, HOLDLOCK`, runs the unchanged single `UPDATE`, reads the prices again and saves a `Products` change per repriced product.
  - **Stock adjustment:** the decorator saves the `BranchStock` quantity change and the `StockAdjustments` history row.
  - Both run in the caller's transaction, or open one, so the history rolls back with the change.
  - The inner classes are untouched, so the existing tests that pin the revision to one `UPDATE` still pass.

### Security events

`SecurityEventRecorder` writes one row per event: who (when known), role, branch, method, path, status, a fixed reason, client IP and correlation ID.

| Event | Recorded where | Reason recorded |
|---|---|---|
| `SignInSucceeded`, `SignedOut` | `AccountController` | — |
| `SignInFailed` | `AccountController`, from `LocalStaffAuthenticator.AuthenticateWithOutcomeAsync` | `MissingCredentials`, `MalformedStaffId`, `UnknownStaff`, `NoPasswordSet`, `WrongPassword`, `Inactive` |
| `AccessDenied` | `AccessDeniedResponder` (403 or the Access Denied redirect); `IntegrationSecurityMiddleware` (missing or wrong API key, IP not allowed) | A fixed sentence |
| `RateLimited` | The rate limiter's `OnRejected` | The policy name |
| `SessionRejected` | `StaffCookieValidator` | `StaffNotFound`, `Inactive`, `RoleChanged`, `BranchChanged`, … |

- **What it never holds:** the password, the API key, the query string or the request body. The path is stored without its query string.
- **Unknown staff IDs are not stored.** For an `UnknownStaff` failure the attempted ID is left empty, because an "ID" that matches nobody is often a password typed into the wrong field.
- **Best-effort, like the request audit.** A security event is not part of any business transaction, so it uses its own context and `CancellationToken.None`. A failed write is logged and never changes the response.

### The application log

Every record is also written to `ILogger` under the category **`ApexPost.Audit`** (`AuditTrailLogger`), after it is stored, with the correlation ID:
- Security events other than a successful sign-in or a sign-out are `Warning`. Requests and data changes are `Information`, with the request's outcome in the message.
- **A data change is logged with its column names, never their values.** Old and new values stay in the database, behind the access rules below; the application log may be shipped to places with weaker access control.

No logging package was added. The category can be routed to any provider through configuration.

### The Audit log screen

`AuditLogController` (`/AuditLog`) has three tabs: **Requests**, **Data changes** and **Security**. Each tab offers:
- filters for period, branch, staff, kind, result and correlation ID;
- paging;
- CSV export of up to 5,000 rows.

**Trace** shows everything one correlation ID left in the three logs, so a single request can be followed from the click to the rows it changed.

| Role | Requests and data changes | Security events |
|---|---|---|
| Auditor, Administrator | Every branch | Yes |
| Branch Manager | Their home branch only | No (403) |
| Counter Clerk | 403 | 403 |

- **The rule is enforced three times:**
  - `[Authorize(Roles = …, Policy = SameBranchOnly)]` refuses a manager who puts another branch in the URL.
  - `[Authorize(Roles = "Auditor,Administrator")]` guards the Security tab.
  - `AuditLogService` applies the same rules again, so an export or a trace cannot reach what the list would refuse.
- **Read-only.** The screen and its queries read only, without tracking. No code path updates or deletes an audit row.
- **Filters are checked.** A reversed period, or one longer than 366 days, is shown as a message with a 400.
- **CSV is safe to open in a spreadsheet.** Every field is quoted, and values starting with `=`, `+`, `-`, `@`, tab or carriage return get a leading `'` (formula injection).
- **Theme and CSP.** The screen uses the shell, theme tokens and icon sprite of ADR 0023. Its styles live in `site.css` (`.audit-*`), with no inline style.

### Schema

The migration `AddAuditTrail` adds `DataChanges` and `SecurityEvents`:
- **No foreign keys.** Like `AuditEntries`, they must outlive the staff and branch rows they mention, and must record unknown IDs.
- **CHECK constraints:** `Operation` and `EventType` must be known values, `ChangesJson` must be valid JSON (`ISJSON`), and the status code must be 100–599.
- **Indexes:** eleven indexes across the three audit tables, one per screen filter: branch + time, time, correlation ID, table + record key, staff + time, and event type + time.

The least-privilege role gets `SELECT, INSERT` only on the two new tables, as on `AuditEntries`.

## Consequences

- **Every committed data change has its history.** It is in the same transaction, so a crash can no longer lose the history of a committed change. The request and security records stay best-effort (ADR 0011).
- **Each write costs more log, but no round trip.** A one-line sale writes 4 history rows. It still sends the same 8 SQL commands, and its transaction log grows from about 2.6 KB to about 9.9 KB (PERFORMANCE.md, "Write overhead of the audit trail"). The test `The_history_rows_ride_in_the_sales_own_batches_and_commit_so_a_sale_makes_no_extra_round_trip` pins the round trips.
- **The history tables grow with every write, and nothing removes rows.** Retention, archiving or partitioning by `OccurredAtUtc` is a deployment decision. The application role cannot delete rows, so a DBA job would do it.
- **Not tamper-evident.** A database administrator can still alter the rows. Where that matters, use Ledger tables or SQL Server Audit, or ship the `ApexPost.Audit` log to a write-once store (docs/security.md).
- **A new table needs no audit code.** It is recorded automatically by the interceptor. A new write that bypasses `SaveChanges` needs its own audited decorator.
- **Tests:**
  - `DataChangeAuditTests`, `AuditTrailSchemaTests` and `AuditLogQueryTests` (SQL Server);
  - `AuditTrailTests` and `AuditLogScreenTests` (web: sign-in events, refusals, access by role, filters, export, trace);
  - `AuditLogServiceTests` (role rules).
