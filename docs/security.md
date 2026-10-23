# Security

## Assignment requirements

- Do not store full identity-document numbers, passwords, API keys, or payment-card data in audit logs.
- MVC form posts must use antiforgery protection.
- Authorization must be enforced server-side; UI visibility alone is insufficient.
- Critical business events must not rely on fire-and-forget work.
- Real credentials and secrets must not be committed.

## Verified implementation

Step 4 includes a Domain `MoneyOrder` entity that stores an identity-verification outcome only; it has no identity-document property or document-number input. Step 7 persists only the money-order identifier, branch code, amount, and verification outcome in Infrastructure JSON DTOs; full identity-document numbers are neither accepted nor serialized. The configured data directory should use ordinary operating-system access controls because it contains operational data, even though this implementation stores no credentials or secrets. Authentication, authorization policy, audit logging, payment processing, custom security middleware, and workflow-level identity verification remain planned constraints for subsequent steps.
