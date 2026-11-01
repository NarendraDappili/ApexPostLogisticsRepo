# ADR 0019: Pin the transitive System.Security.Cryptography.Pkcs package to a patched version

Status: Accepted by the team; **needs trainer approval**, because the package is outside the brief's allowed list.

## Context

The brief allows only `Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`, `Microsoft.EntityFrameworkCore.*`, xUnit and `Microsoft.AspNetCore.Mvc.Testing`. Anything else needs trainer approval and an ADR.

`Microsoft.EntityFrameworkCore.SqlServer` 10.0.13 depends on `Microsoft.Data.SqlClient` 6.1.7, which depends on `System.Security.Cryptography.Pkcs`. Without intervention, NuGet resolves that package to 9.0.11, which has published advisories (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx). The zero-warning build (`dotnet build -warnaserror`, as CI runs it) fails on the vulnerability warning (NU1903). Shipping a known-vulnerable dependency would be wrong anyway.

The two advisories are .NET denial-of-service vulnerabilities, CVE-2026-70162 and CVE-2026-84008. Each affects `System.Security.Cryptography.Pkcs` 9.0.0–9.0.20 and 10.0.0–10.0.12; the patched versions are 9.0.21 and 10.0.13.

**No allowed package avoids the pin.** As checked against nuget.org on 1 November 2026:
- `Microsoft.Data.SqlClient` 6.1.7 still requests `Pkcs` 9.0.11 for .NET 9 and later.
- The newest EF Core 10 patch, `Microsoft.EntityFrameworkCore.SqlServer` 10.0.14, still depends on SqlClient 6.1.7.

## Options

1. **Pin the transitive package to the patched 10.0.13** with a direct `PackageReference` in `ApexPost.Infrastructure.csproj`.
2. **Suppress the warning.** This hides a real vulnerability.
3. **Downgrade EF Core** to a version whose SqlClient brings a patched package. This would leave EF Core 10, which the brief requires.

## Decision

Option 1. The reference is a security pin only:
- No code uses the package directly.
- It adds no new dependency to the application. The package is already in the graph through SqlClient; only its version changes.
- `dotnet nuget why src/ApexPost.Infrastructure/ApexPost.Infrastructure.csproj System.Security.Cryptography.Pkcs` shows both paths: through `Microsoft.Data.SqlClient` 6.1.7, and the direct pin. Both resolve to 10.0.13.
- The project file carries a comment naming the advisories and the condition for removing the pin.

## Consequences

- **The build stays at zero warnings with no suppressed vulnerability warning.**
- **The pin must be removed** once `Microsoft.Data.SqlClient` references a patched version by itself. Otherwise it can hold the package back from later fixes. `dotnet list package --vulnerable --include-transitive` shows when that point is reached.
- **Pending approval:** until the trainer approves, this is the one dependency outside the allowed list. It is recorded here rather than hidden.
