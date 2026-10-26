# Security

## Assignment requirements

- Do not store full identity-document numbers, passwords, API keys, or payment-card data in audit logs.
- MVC form posts must use antiforgery protection.
- Authorization must be enforced server-side; UI visibility alone is insufficient.
- Critical business events must not rely on fire-and-forget work.
- Real credentials and secrets must not be committed.

## Verified implementation

`MoneyOrder` retains sender, recipient, fee, transaction context, and an identity-verification record. A full document number is accepted only as transient Application request input, immediately converted to a masked reference (`****` plus up to four trailing characters). Domain and Infrastructure JSON retain document type, result, context ID, and the masked reference; they do not retain a full document number. The configured data directory should use ordinary operating-system access controls. Cookie authentication, authorization policies, audit logging, and custom security middleware are implemented; payment processing and external identity-provider integration are not.

The repository tracks the JSON snapshots in `src/ApexPost.Web/App_Data/`, including audit and runtime business records; the checked-in data is synthetic. Running the application may update those tracked files. Review all such diffs before committing and do not publish real staff/customer information or production data. The `data-protection-keys/` subdirectory is explicitly excluded from Git: generated keys are sensitive local material and must never be committed, pushed, or copied into a shared repository. A separate clone or deployment generates its own key ring, so protected cookies are not portable between those environments.

## MVC boundary

All MVC POST actions use antiforgery validation and bind dedicated view models rather than Domain entities. The money-order form uses a password-type input for the transient document number and clears it before re-rendering an application error; the full number is not placed in TempData, logs, views after failure, or JSON. ASP.NET Core data-protection keys are generated locally below the configured data directory and excluded from source control. UI visibility is never treated as authorization.

## Cookie authentication and authorization

Cookie authentication uses only the built-in ASP.NET Core framework; ASP.NET Core Identity and third-party packages are not used. A successful local-demo sign-in issues claims for the staff ID, `StaffRole`, and assigned branch. The cookie is HttpOnly, SameSite=Strict, has an eight-hour sliding expiry, and uses `SameAsRequest` secure policy so local HTTP development remains usable while HTTPS requests receive a Secure cookie. Deployments must terminate or serve HTTPS.

Committed configuration contains only the `ApexPost:Authentication:DemoAccounts` key shape, never a password. Local demo password values belong in user secrets. The local credential comparison is constant-time and login errors are deliberately generic. The staff record is loaded when signing in and on each authenticated request; missing, inactive, role-changed, or branch-changed staff records reject the cookie. This mitigates stale privilege after deactivation but does not provide central session revocation across separate application processes sharing JSON files.

Authorization is enforced at endpoints, not by navigation visibility: Counter Clerks are constrained to their claimed branch for checkout, shipping, and money orders; `SameBranchOnly` checks route/query branch access; `BranchManagerOnly` guards void/refund endpoints and the service still requires an active assigned manager plus a non-empty reason. The checkout form does not submit staff identity; the controller uses the StaffId claim for the Application request. Auditors and Administrators may view their own-branch report page but have no transaction authority. Rate limiting and JSON audit records are implemented; account lockout, password hashing/rotation, MFA, tamper-evident audit retention, TLS termination configuration, and production identity integration remain operational requirements.

## Pipeline controls

Unhandled exceptions are logged server-side with a correlation ID and returned as a generic RFC 7807-style Problem Details response containing only `traceId` and `correlationId`; stack traces, internal exception messages, credentials, and sensitive business input are not returned. Request timing uses `try/finally`, so failed requests are timed and logged too. Valid `X-Correlation-Id` input is limited to 64 ASCII alphanumeric, hyphen, or underscore characters before reuse; other values are replaced.

Responses receive CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: strict-origin-when-cross-origin` headers. HSTS is enabled outside Development and HTTPS redirection remains active. Maintenance mode is configuration-driven and preserves `/health` for infrastructure checks. The integration API key is compared in constant time and is never logged; missing/invalid keys return 401, while an absent required key configuration returns 500 so deployment faults are visible.

IP allowlisting is endpoint metadata applied to integration and manager-reversal endpoints. It uses the direct connection address and deliberately does not trust forwarded headers by default. Behind a reverse proxy, configure ASP.NET Core forwarded-header middleware with explicit known proxies/networks before relying on client IP restrictions; accepting arbitrary `X-Forwarded-For` would let callers spoof their address. The login uses a fixed-window per-IP limit. Integration requests must pass API-key validation before they consume the fixed-window aggregate integration limit; the limiter uses one constant partition and ignores caller-provided `X-Partner-Id`, avoiding unbounded attacker-controlled partition creation. The IP allowlist runs after that limiter, so a valid-key request from a blocked address still consumes an integration permit. CORS allows only the configured partner widget origin and is not a substitute for API-key authentication. The production CSP currently permits inline scripts/styles for Razor/bootstrap compatibility and should be tightened with nonces or hashes when the frontend is revised.

## Audit data handling

The global MVC audit filter records the authenticated staff ID, role, assigned branch, controller/action, safe identifier-only route values, result status, duration, outcome, and correlation ID. It never reads or serializes action arguments, form values, headers, request bodies, exception details, or view models. Therefore password, full identity-document, API-key, and payment-card fields cannot enter audit records through the filter. This includes failed validation and login attempts: the fact and outcome are recorded, not submitted credentials.

`audit-records.json` is protected only by ordinary application/operating-system file access controls. Its append-only Application API is not tamper-proof: a principal with filesystem write access can alter or delete it, and the in-process lock does not coordinate separate hosts/processes. JSON audit data must not be represented as an immutable ledger. Production deployments need restricted filesystem access, retention/backups, monitoring, and an integrity-protected/off-host audit sink where tamper evidence or immutability is required.

The MVC audit filter currently treats persistence as best-effort: it logs append errors, including malformed audit JSON and I/O failures, but does not fail a business action that has already executed. This avoids presenting a failed action result after a successful mutation, but means audit completeness is not guaranteed. Monitor the logged failures and use a durable, integrity-protected audit service before production use.

## Cancellation and persistence review

When checkout has already removed stock, a later transaction-save failure triggers stock compensation using a non-cancelable token. Refund stock restoration likewise continues after its status has been persisted. This is an in-process consistency measure, not an atomic transaction: a process crash, filesystem failure, or failed compensation still requires reconciliation. The caller's cancellation token continues to govern validation and the original transaction operation; only the compensating inventory writes are allowed to finish independently.

The JSON store writes a temporary file beside the target, serializes and flushes asynchronously, then replaces the target. Failed replacement attempts clean up the temporary file when possible and propagate the error; malformed or empty JSON is reported rather than silently replaced. Per-path semaphores coordinate only within the current process. There is no cross-process lock, database transaction, automatic corruption recovery, or guarantee across multiple files. Protect data files and backups with OS-level permissions and recovery procedures.

Request-abort cancellation is not returned as a generic 500 Problem Details response; the server records an internal 499 status and informational event without attempting to write a body to the disconnected client. Other unexpected exceptions return generic Problem Details. Server logs record the correlation ID and exception type, but not the exception object, message, or stack trace, reducing the chance of sensitive values in exception details reaching application logs. Request duration logging runs in `finally`, including failed and aborted requests.

IP allowlists use the direct remote connection address. No forwarded-header middleware or trusted proxy list is configured in this application, so deployments behind a proxy must not rely on the allowlist until they configure trusted proxies/networks and forwarded-header processing safely. CORS remains a browser policy, not authentication. API-key validation precedes the shared process-local integration limiter; allowlist rejection follows it, so a valid-key blocked request consumes a permit.

## Delivery configuration boundary

No password or API-key value is committed. `ApexPost:Authentication:DemoAccounts:<StaffId>` and `ApexPost:Security:Integration:ApiKey` belong in user secrets or deployment secret storage. The application has no configured source-controlled demo password, even though the seed data contains synthetic staff IDs. Deploy over TLS and configure trusted proxy/forwarded-header behavior before relying on direct-client IP allowlisting behind a reverse proxy. See the [README](../README.md) for commands and [DESIGN.md](../DESIGN.md) for threat/mitigation and deployment notes.
