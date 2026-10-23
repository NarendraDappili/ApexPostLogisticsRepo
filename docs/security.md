# Security

## Assignment requirements

- Do not store full identity-document numbers, passwords, API keys, or payment-card data in audit logs.
- MVC form posts must use antiforgery protection.
- Authorization must be enforced server-side; UI visibility alone is insufficient.
- Critical business events must not rely on fire-and-forget work.
- Real credentials and secrets must not be committed.

## Verified implementation

Step 2 contains the minimal ASP.NET Core MVC template only. No application authentication, authorization policy, identity verification, audit logging, payment processing, or custom security middleware has been implemented. The requirements above remain planned constraints for subsequent steps.
