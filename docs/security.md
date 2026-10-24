# Security

## Assignment requirements

- Do not store full identity-document numbers, passwords, API keys, or payment-card data in audit logs.
- MVC form posts must use antiforgery protection.
- Authorization must be enforced server-side; UI visibility alone is insufficient.
- Critical business events must not rely on fire-and-forget work.
- Real credentials and secrets must not be committed.

## Verified implementation

Step 11 extends `MoneyOrder` with sender, recipient, fee, transaction context, and an identity-verification record. A full document number is accepted only as transient Application request input, immediately converted to a masked reference (`****` plus up to four trailing characters). Domain and Infrastructure JSON retain document type, result, context ID, and the masked reference; they do not retain a full identity-document number. The configured data directory should use ordinary operating-system access controls because it contains operational data, even though this implementation stores no credentials, passwords, API keys, or payment-card data. Authentication, authorization policy, audit logging, payment processing, custom security middleware, and external identity-provider integration remain planned constraints for subsequent steps.

## Step 14 MVC boundary

All MVC POST actions use antiforgery validation and bind dedicated view models rather than Domain entities. The money-order form uses a password-type input for the transient document number and clears it before re-rendering an application error; the full number is not placed in TempData, logs, views after failure, or JSON. ASP.NET Core data-protection keys are generated locally below the configured data directory and excluded from source control. Step 15 adds authentication and authorization; UI visibility is still never treated as authorization.

## Step 15 cookie authentication and authorization

Cookie authentication uses only the built-in ASP.NET Core framework; ASP.NET Core Identity and third-party packages are not used. A successful local-demo sign-in issues claims for the staff ID, `StaffRole`, and assigned branch. The cookie is HttpOnly, SameSite=Strict, has an eight-hour sliding expiry, and uses `SameAsRequest` secure policy so local HTTP development remains usable while HTTPS requests receive a Secure cookie. Deployments must terminate or serve HTTPS.

Committed configuration contains only the `ApexPost:Authentication:DemoAccounts` key shape, never a password. Local demo password values belong in user secrets. The local credential comparison is constant-time and login errors are deliberately generic. The staff record is loaded when signing in and on each authenticated request; missing, inactive, role-changed, or branch-changed staff records reject the cookie. This mitigates stale privilege after deactivation but does not provide central session revocation across separate application processes sharing JSON files.

Authorization is enforced at endpoints, not by navigation visibility: Counter Clerks are constrained to their claimed branch for checkout, shipping, and money orders; `SameBranchOnly` checks route/query branch access; `BranchManagerOnly` guards void/refund endpoints and the service still requires an active assigned manager plus a non-empty reason. Auditors and Administrators may view their own-branch report page but have no transaction authority in this step. Brute-force throttling, account lockout, password hashing/rotation, MFA, audit trails, TLS termination configuration, and production identity integration remain future operational requirements.

## Step 16 pipeline controls

Unhandled exceptions are logged server-side with a correlation ID and returned as a generic RFC 7807-style Problem Details response containing only `traceId` and `correlationId`; stack traces, internal exception messages, credentials, and sensitive business input are not returned. Request timing uses `try/finally`, so failed requests are timed and logged too. Valid `X-Correlation-Id` input is limited to 64 ASCII alphanumeric, hyphen, or underscore characters before reuse; other values are replaced.

Responses receive CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: strict-origin-when-cross-origin` headers. HSTS is enabled outside Development and HTTPS redirection remains active. Maintenance mode is configuration-driven and preserves `/health` for infrastructure checks. The integration API key is compared in constant time and is never logged; missing/invalid keys return 401, while an absent required key configuration returns 500 so deployment faults are visible.

IP allowlisting is endpoint metadata applied to integration and manager-reversal endpoints. It uses the direct connection address and deliberately does not trust forwarded headers by default. Behind a reverse proxy, configure ASP.NET Core forwarded-header middleware with explicit known proxies/networks before relying on client IP restrictions; accepting arbitrary `X-Forwarded-For` would let callers spoof their address. Login and partner endpoints use fixed-window rate limits; CORS allows only the configured partner widget origin and is not a substitute for API-key authentication. The production CSP currently permits inline scripts/styles for Razor/bootstrap compatibility and should be tightened with nonces or hashes when the frontend is revised.

## Step 17 audit data handling

The global MVC audit filter records the authenticated staff ID, role, assigned branch, controller/action, safe identifier-only route values, result status, duration, outcome, and correlation ID. It never reads or serializes action arguments, form values, headers, request bodies, exception details, or view models. Therefore password, full identity-document, API-key, and payment-card fields cannot enter audit records through the filter. This includes failed validation and login attempts: the fact and outcome are recorded, not submitted credentials.

`audit-records.json` is protected only by ordinary application/operating-system file access controls. Its append-only Application API is not tamper-proof: a principal with filesystem write access can alter or delete it, and the in-process lock does not coordinate separate hosts/processes. JSON audit data must not be represented as an immutable ledger. Production deployments need restricted filesystem access, retention/backups, monitoring, and an integrity-protected/off-host audit sink where tamper evidence or immutability is required.
