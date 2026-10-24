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

All MVC POST actions use antiforgery validation and bind dedicated view models rather than Domain entities. The money-order form uses a password-type input for the transient document number and clears it before re-rendering an application error; the full number is not placed in TempData, logs, views after failure, or JSON. ASP.NET Core data-protection keys are generated locally below the configured data directory and excluded from source control. The login page is a labelled UI placeholder only: there is no authentication, authorization, role enforcement, or assumption that hiding a screen protects an operation.
