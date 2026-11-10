# Security

## Requirements

- **No secrets in source control:** no real credentials, connection strings or API keys.
- **Identity documents stay masked everywhere:** database, logs and audit entries.
- **Audit records contain no passwords**, document numbers, API keys or payment-card data.
- **No string-concatenated SQL:** EF Core LINQ, parameterised raw SQL and parameterised procedures only.
- **Forms are protected:** every MVC form post uses antiforgery protection.
- **The server authorises every request;** what the UI shows or hides is not a control.
- **Fictional data only.**

## Secrets and configuration

| Secret | Where it lives |
|---|---|
| `ConnectionStrings:ApexPostDb` | User-secrets in Development (`dotnet user-secrets set --project src/ApexPost.Web …`); the deployment's secret store or the `ConnectionStrings__ApexPostDb` environment variable elsewhere. The app refuses to start without it. `dotnet ef` reads the same user-secret through `ApexPostDesignTimeDbContextFactory`. |
| `ApexPost:Security:Integration:ApiKey` | User-secrets or the deployment secret store. Compared in constant time and never logged. |
| Development demo password | User-secrets only (`ApexPost:DemoSeed:Password`); nothing is committed. `scripts/setup-dev.ps1` generates a random one when none is set. Without it the seeder seeds nothing and logs how to set it. Each staff row stores its own PBKDF2 hash, never the password. The value committed before Week 3's review (`ApexPost-Demo-2026`) stays in the Git history, so treat it as public: never reuse it, and change the user-secret of any local database still seeded with it. |
| CI database password | Generated per run (`openssl rand`), masked in the log, and gone with the throwaway container. |
| Data-protection keys | Generated locally under `App_Data/data-protection-keys` and ignored by Git, so a clone or deployment gets its own key ring. |

The committed configuration holds non-secret defaults only. Business data lives in SQL Server, not in the repository. `App_Data` holds only the local key ring.

## Database security

### Least privilege

[database/security/apexpost-app-role.sql](../database/security/apexpost-app-role.sql) creates the role `apexpost_app` for the application's database user. The application login is not `db_owner` and is not in `db_datareader`/`db_datawriter`. Migrations and the Development seed run under a separate deployment or developer login.

| Allowed | Not allowed |
|---|---|
| `SELECT`/`INSERT`/`UPDATE` only on the tables the use cases write | `DELETE` on any table (records are closed or deactivated, never deleted) |
| `SELECT` on reference data | Any DDL |
| `SELECT, INSERT` on `AuditEntries`, `DataChanges` and `SecurityEvents` (append-only for the app) | `UPDATE`/`DELETE` on any audit table |
| `EXECUTE` on the four procedures | Any direct access to `StockAdjustments`: history is written only by `usp_AdjustStock`, through ownership chaining |

This was verified by impersonating a member of the role ([ADR 0012](adr/0012-stored-procedures-for-reports-and-stock-adjustment.md)):
- **Allowed:** the reads, the price-revision `UPDATE`, the audit `INSERT` and the procedures.
- **Refused with Msg 229 (no permission):** `UPDATE`/`DELETE` on audit rows, `DELETE` on sales, and direct access to `StockAdjustments`.
- **Refused with Msg 262 (no DDL permission):** `CREATE TABLE`.

The check was repeated for the audit trail of [ADR 0024](adr/0024-complete-audit-trail-and-audit-log-screen.md), on a new database built by the idempotent script, with the role script run twice:
- **Allowed:** `INSERT` into `DataChanges` and `SecurityEvents`, `SELECT` from all three audit tables, and the `UPDLOCK, HOLDLOCK` read of `Products` that the audited price revision uses.
- **Refused with Msg 229:** `UPDATE` and `DELETE` on `DataChanges` and `SecurityEvents`, `DELETE` on `AuditEntries`, and `SELECT` on `StockAdjustments`.

### No SQL injection surface

- **EF Core LINQ** sends parameters for every value. Code parameters are typed `varchar(48)` like their columns.
- **Raw SQL** uses only `SqlQuery`/`ExecuteSql` with interpolated strings, which EF Core turns into `DbParameter`s. Examples: `EXEC dbo.usp_DailyBranchSales @BranchCode = {code}, @Date = {day}` and `usp_AdjustStock`.
- **The procedures** use static SQL only.
- **The migrations** deploy procedure bodies held as constants in each migration, with `EXEC(N'…')`. That text is fixed in source control and contains no user input.
- **The scripts in `/sql` and `database/perf`** take their inputs as T-SQL variables.

### Tests never touch the development database

Integration tests create `ApexPostDb_Test_<guid>` databases and drop them, and a guard refuses any database whose name lacks that prefix.

## Identity documents

A full document number is accepted only as transient request input on the verification step. It never leaves memory unmasked:

- **Domain:** `MaskedIdentityDocumentReference.FromDocumentNumber` keeps `****` plus at most the last four characters. The domain has no type that can hold a full number.
- **Mapping:** `MoneyOrderMapper` writes only the masked value. When reading, it rejects any stored value that is not masked (`FromMaskedValue`) ([ADR 0016](adr/0016-domain-and-persistence-model-separation.md)).
- **Schema:** `IdentityVerifications.MaskedDocumentReference` is `varchar(8)`, so a full number cannot fit, and `CK_IdentityVerifications_Masked` requires the `****` prefix.
- **Web:** the form uses a password-type input and clears it before re-rendering after an error. The number is never placed in TempData, views, logs or audit records. The audit filter does not read form values at all.
- **Tests:**
  - A SQL test checks that only the masked reference reaches the database.
  - A model test pins the `varchar(8)` column, the CHECK constraint and the absence of any document-number property.
  - A mapper test checks that unmasked storage is refused.
  - Web tests check that a submitted document number and password are absent from logs and audit records.

## Authentication and authorisation

**Cookie authentication.** It uses only the built-in ASP.NET Core cookie handler; there is no ASP.NET Core Identity or third-party package.
- A successful sign-in issues claims for the staff ID, role and home branch.
- The cookie is HttpOnly and SameSite=Strict, with an eight-hour sliding expiry.
- **Outside Development, the sign-in and antiforgery cookies are always `Secure`** (`CookieSecurePolicy.Always`), so a browser never sends them over plain HTTP, even on a first `http://` request before the HTTPS redirect. Development keeps `SameAsRequest` so `http://localhost` and the test host work. A test hosts the app as Production and checks both cookie settings.
- **Consequence behind a TLS-terminating proxy:** antiforgery refuses to issue an HTTPS-only cookie on a request the app sees as HTTP. Forwarded headers must therefore be configured with trusted proxies (see "IP allowlist" below) before the forms work there.

**Passwords.**
- Each `Staff` row stores a PBKDF2-HMAC-SHA256 hash: 210,000 iterations and a random 16-byte salt.
- Verification is constant-time.
- An unknown staff ID costs the same hashing work as a wrong password, and sign-in errors are generic.

**Cookie re-validation.** Every authenticated request re-reads the staff row from SQL Server. A missing or inactive staff member, or a changed role or branch, invalidates the cookie, so deactivation takes effect at once on every server.

**Authorisation.**
- **Policies:** `BranchManagerOnly` guards void and refund. `SameBranchOnly` restricts Counter Clerks and Branch Managers to their home branch. Administrators and Auditors may view any branch.
- **Controller checks:** controllers compare a submitted branch code with the cookie's branch, and return 403 (`Forbid`) if they differ.
- **Application services** remain the final authority on manager assignment, active status, transaction transitions and reversal reasons.
- **Identity comes from the cookie, never from the form.** Checkout, voids, refunds and stock adjustments take the acting staff member from the cookie.

**Over-posting.** Input models contain no status, key or audit fields; integration tests prove that posting them has no effect.

## Pipeline controls

- **Errors:**
  - Business errors become 409/404/400 Problem Details with a message written for the user ([ADR 0018](adr/0018-exceptions-to-problem-details-status-codes.md)).
  - Every other exception becomes a 500 Problem Details carrying only `traceId` and `correlationId`. It never includes the exception message, stack trace or input.
  - Server logs record the correlation ID and exception type, not the message.
  - A client abort is recorded as an internal 499, and no response body is written.
- **Correlation ID:** a valid `X-Correlation-Id` (at most 64 ASCII letters, digits, `-` or `_`) is reused; anything else is replaced.
- **Headers:**
  - CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy` (camera, microphone, geolocation, payment and USB off) and `Cross-Origin-Opener-Policy: same-origin`.
  - HSTS outside Development, and HTTPS redirection.
  - **The CSP allows no inline script or style:** `script-src 'self'; style-src 'self'`, with no `'unsafe-inline'`. Even if markup were injected into a page, its `<script>` blocks, `onerror=` handlers and `javascript:` links would not run.
    - This works because the pages need none: every script is a file under `wwwroot`, and the layout's empty import map was removed, since no page uses JavaScript modules.
    - Bootstrap, Popper and jQuery validation still work: they change styles through the DOM (`element.style.display = …`), which the CSP allows. It blocks only style attributes and `<style>` elements.
    - A test scans every `.cshtml` file and the rendered layout, so adding an inline script or style attribute fails the build instead of silently breaking the page.
    - **The UI shell keeps to the same policy** ([ADR 0023](adr/0023-application-shell-and-theme-under-the-strict-csp.md)). The theme is one same-origin stylesheet, the icons are an inline SVG sprite (markup, not script or style), and the logo watermark is a same-origin image (`img-src 'self'`). The policy did not need widening, and no CDN or font origin was added.
    - **The menu and branch picker are not a security boundary.** They only stop offering what the server would refuse; `[Authorize]`, `SameBranchOnly` and `BranchManagerOnly` still decide every request.
- **Rate limits** (fixed one-minute windows, 429 when exceeded, each one recorded as a security event):
  - **Sign-in:** 5 posts a minute per IP.
  - **Integration, per partner:** 60 requests a minute for the partner its `X-Api-Key` proves. The partition is a fingerprint of the configured key; the caller-supplied `X-Partner-Id` is ignored.
  - **Wrong or missing keys:** 10 a minute per client IP, so guessing a key is limited and never spends a partner's budget ([ADR 0004](adr/0004-order-security-middleware-before-endpoints.md)).
- **IP allowlist:** it applies to the admin screens and the integration endpoint. It is loopback-only in Development and empty (deny) elsewhere.
  - It uses the direct connection address.
  - Behind a reverse proxy, configure forwarded-header processing with explicit trusted proxies before relying on it. Accepting arbitrary `X-Forwarded-For` would let callers spoof their address.
- **CORS** allows only the configured partner widget origin. It is not authentication.
- **Maintenance mode** keeps `/health` available.

## Audit data

`AuditResourceFilter` writes one record per MVC request. A record holds:
- staff ID, role and branch;
- controller and action;
- allow-listed route IDs;
- the status code the client received;
- duration, correlation ID and outcome.

The filter never reads action arguments, form values, headers, request bodies, models or exception details. Credentials and document numbers therefore cannot reach the audit trail, including from failed sign-ins and invalid forms.

Records are stored in the `AuditEntries` table ([ADR 0011](adr/0011-audit-store-outside-the-unit-of-work.md)):

- **Separate commit.** Each append uses its own short-lived `DbContext`, outside the request's unit of work. An audit write can neither commit a failed business change nor undo a committed one.
- **Append-only for the application.** The least-privilege role grants only `SELECT, INSERT`, so the application cannot alter or delete audit rows. A database administrator still can. Where tamper evidence is required, use SQL Server Ledger tables, SQL Server Audit, or ship the records to an off-host, write-once store.
- **Best-effort.** A failed append is logged with the controller and action and does not change the response. Monitor those log entries. A crash between the business commit and the audit insert loses that record; an outbox would be needed for guaranteed audit.

### Data changes and security events

[ADR 0024](adr/0024-complete-audit-trail-and-audit-log-screen.md) adds two more append-only records.

**`DataChanges`: one row per inserted, updated or deleted table row.**
- **What a row holds:** the business key, the operation, the staff member, the branch, the correlation ID, and the old and new value of each changed column.
- **Same transaction as the change.** A rolled-back change leaves no history.
- **What it never holds:**
  - **Password hash:** recorded only as `(hidden)`, so a password change is visible without its hash.
  - **Identity documents:** only the masked reference exists to be recorded.
  - **Audit columns and row versions:** left out.

**`SecurityEvents`: sign-ins, sign-outs and refusals.**
- **What is recorded:**
  - successful and failed sign-ins, with a fixed reason such as `WrongPassword` or `Inactive`;
  - sign-outs;
  - 403s and Access Denied redirects;
  - refused API keys and IP addresses;
  - rate-limited requests;
  - rejected sign-in cookies.
- **What a row holds:** method, path without the query string, status, client IP, correlation ID, and the staff member when known.
- **Unknown IDs are not stored.** For an unknown staff ID the attempted ID is not stored, because it may be a password typed into the wrong field.
- **Never recorded:** passwords, API keys and request bodies.

**The application log.** Every audit record is also written to `ILogger` under the category `ApexPost.Audit`. A data change is logged with its column names only, never the values, because log destinations may be less protected than the database.

**Who can read the trail.** The Audit log screen (`/AuditLog`):
- **Auditors and Administrators:** every branch and all three logs.
- **Branch Managers:** requests and data changes of their home branch only, and no security events.
- **Counter Clerks:** refused (403).

`SameBranchOnly`, a role check on the Security tab, and `AuditLogService` each enforce this, including for the CSV export and the trace view.

**CSV export.** The export quotes every field and neutralises spreadsheet formulas: a value starting with `=`, `+`, `-`, `@`, tab or carriage return gets a leading `'`.

## Data integrity under concurrency

- **Concurrent changes are refused, not merged.** Stock and sales use `rowversion` optimistic concurrency, and each use case commits in one database transaction ([ADR 0017](adr/0017-optimistic-concurrency-for-stock-and-transactions.md)). A concurrent change is refused with 409 and nothing is half-saved.
- **No reconciliation is needed.** The Week 2 compensation path, and the reconciliation it could require, no longer exist.
- **Stock cannot go negative from any source.** The domain, a CHECK constraint and the stock procedure each refuse it.

## Deployment checklist

- Supply the connection string and API key from a secret store, never from committed files.
- Create a database user for the app in the `apexpost_app` role. Run migrations under a separate login.
- Serve over TLS, and configure trusted proxies before relying on the IP allowlist behind a proxy.
- Keep `ApexPost:DemoSeed:Enabled` off outside Development. The seeder runs only in Development regardless.
- Monitor the logs for "Unable to append MVC audit record" and "Unable to append security event".
- Route the `ApexPost.Audit` log category to the monitoring or write-once store, and alert on its `Warning` entries (refused sign-ins, access denied, rate limits).
- Decide a retention period for `AuditEntries`, `DataChanges` and `SecurityEvents`; the application never deletes their rows.
