# ADR 0003: Use local cookie authentication with persisted staff revalidation

Status: Accepted — documented at Step 20.

## Context

The application needs roles and branch-scoped access without introducing ASP.NET Core Identity or external identity infrastructure.

## Options considered

- Built-in cookie authentication with configured local demo credentials.
- ASP.NET Core Identity or external identity provider.
- UI-only role visibility.

## Decision

Use built-in cookie authentication. `LocalStaffAuthenticator` creates claims from a persisted active staff record, and `StaffCookieValidator` reloads that record on each request.

## Consequences

The application enforces server-side role/branch access and invalidates stale local cookies. Password provisioning, MFA, lockout, and distributed session revocation remain out of scope.
