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
