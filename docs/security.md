# Security

## Assignment requirements

- Do not store full identity-document numbers, passwords, API keys, or payment-card data in audit logs.
- MVC form posts must use antiforgery protection.
- Authorization must be enforced server-side; UI visibility alone is insufficient.
- Critical business events must not rely on fire-and-forget work.
- Real credentials and secrets must not be committed.

## Verified implementation

Step 4 includes a Domain `MoneyOrder` entity that stores an identity-verification outcome only; it has no identity-document property or document-number input. Authentication, authorization policy, audit logging, payment processing, custom security middleware, and workflow-level identity verification remain planned constraints for subsequent steps.
