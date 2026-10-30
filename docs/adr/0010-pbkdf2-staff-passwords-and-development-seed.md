# ADR 0010: Store PBKDF2 password hashes on staff records and seed demo data in Development

Status: Accepted.

## Context

The review found that the app could not be demonstrated from a fresh clone: there was no `shipping-rates.json`, every demo login needed a plain-text password configured per staff ID in user secrets, and those passwords were compared directly. Runtime data, including more than a thousand audit records, was committed.

## Options considered

- Keep per-staff passwords in configuration and document more setup steps.
- Store a salted, slow hash on each staff record and seed a complete demo data set automatically in Development.

## Decision

- `StaffMember.PasswordHash` holds a PBKDF2-HMAC-SHA256 hash (210,000 iterations, 16-byte random salt, format `PBKDF2-SHA256$iterations$salt$hash`) produced by `Pbkdf2PasswordHasher` behind `IPasswordHasher`. `LocalStaffAuthenticator` verifies in constant time, and verifies against a throwaway hash when the staff ID is unknown so timing does not reveal which IDs exist.
- In Development only, `DemoDataSeedHostedService` runs `DemoDataSeeder` before the server accepts requests when `ApexPost:DemoSeed:Enabled` is true and the data folder is empty. It writes branches, counters, 20 staff with hashed demo passwords, 30 products, stock, 60 shipping rates, and 40 historical transactions with their shipments and money orders. Existing data is never modified.
- `src/ApexPost.Web/App_Data/` is runtime data and is ignored by Git.

## Consequences

- `dotnet run` on a fresh clone gives a working demo with documented logins; no user-secrets step is needed to sign in.
- The demo password lives in `appsettings.Development.json`. It protects synthetic data in a local Development run only; the seeder never runs in other environments, and the value can be overridden with user secrets.
- Administrators set and reset passwords through the staff screens; only hashes are persisted.
- Integration tests also exposed that `WebApplicationFactory` configuration added through `ConfigureAppConfiguration` is not visible to settings `Program.cs` reads before `Build()`, so the tests had been writing into `App_Data`. They now pass the data directory with `UseSetting`.
