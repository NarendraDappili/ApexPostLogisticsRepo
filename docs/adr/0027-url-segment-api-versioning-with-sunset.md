# ADR 0027: The tracking API is versioned by URL segment, with a published sunset

Status: Accepted (Week 4, Part C).

## Context

v2 of the tracking API changes the response shape in ways that break clients: the nested recipient, and enums written as names. Partners and the customer portal already call the Part B contract. They must keep working while they move, and they must learn about the move from the API itself, not only from an email.

## Decision

- **URL-segment versioning with Asp.Versioning:** `/api/v1/…`, `/api/v2/…`. The version is visible in every log line and link, easy to try in a browser or `.http` file, and gives each version its own OpenAPI document. A header or media-type version would hide the version from all three.
- **One controller per version and resource** (`Controllers/V1`, `Controllers/V2`), with response DTOs per version. Both versions call the same `ShipmentTrackingService`, and the request DTOs are shared. Tests require that the two versions have the same routes, operation IDs and requests, and that v2 never returns a v1 response type.
- **v1 is marked deprecated in three places:**
  - its controllers have `[ApiVersion(1.0, Deprecated = true)]`;
  - a deprecation policy (since 9 November 2026) and a sunset policy (31 May 2027), each with a link to the versioning policy, write the `Deprecation`, `Sunset` and `Link` headers on v1's responses;
  - `ReportApiVersions` writes `api-supported-versions` and `api-deprecated-versions` on every response.
- **The unversioned Part B URLs are an alias of v1** until v1's sunset. This is a rewrite (`/api/shipments…` → `/api/v1/shipments…`), not a second set of endpoints, so it is in no OpenAPI document.
- **The OpenAPI documents come from the built-in generator,** one per API Explorer group (`v1`, `v2`), not from Asp.Versioning's OpenAPI package. That package was measured against the v1 document: it dropped the request body descriptions, every example and the numeric enums' field descriptions, because it replaces the .NET XML-comment generator. Its analyzer warnings (AV0029, AV0030) are suppressed in the project file, with that reason next to them. The deprecation notice and the `deprecated` flags are two small transformers instead.

## Consequences

- **No Part B client breaks on the day v2 ships.** Clients see the plan in every response's headers, in the v1 document and in Swagger UI. [VERSIONING.md](../../VERSIONING.md) has the schedule for retiring v1.
- **The controllers are written twice.** The second copy is mechanical, and a test keeps the two in step. A v3 would add a third set; v1's removal deletes one.
- **A deprecated version only answers errors with the version headers, not with `Deprecation` and `Sunset`.** That is Asp.Versioning's behaviour for error responses. Successful responses, which every client receives, carry all of them.
- **Removing v1 is a deletion:** its controllers, its response DTOs, the rewrite and its policies.
