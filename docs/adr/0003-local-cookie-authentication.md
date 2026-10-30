# ADR 0003: Use local cookie authentication with persisted staff revalidation

Status: Accepted.

## Context

The application needs roles and branch-scoped access without introducing ASP.NET Core Identity or external identity infrastructure.

## Options considered

- Built-in cookie authentication with configured local demo credentials.
- ASP.NET Core Identity or external identity provider.
- UI-only role visibility.

## Decision

Use built-in cookie authentication. `LocalStaffAuthenticator` verifies the PBKDF2 password hash stored on the staff record (see ADR 0010) and creates claims from a persisted active staff record, and `StaffCookieValidator` reloads that record on each request. Denied page navigation redirects to Access Denied; a denied state-changing request (for example a clerk posting a void) receives a plain 403.

## Consequences

The application enforces server-side role/branch access and invalidates stale local cookies, including after an administrator changes a role, home branch or active status. Administrators provision and reset passwords; MFA, lockout, and distributed session revocation remain out of scope.
