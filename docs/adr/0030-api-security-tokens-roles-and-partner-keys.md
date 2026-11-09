# ADR 0030: The tracking API is secure by default: JWT bearer tokens with roles, and API keys for one partner route

Status: Accepted (Week 4, Part D).

## Context

Until Part D, anyone who could reach the tracking API could call every endpoint. The brief asks for:
- JWT bearer authentication, with Courier and Admin roles;
- a check that couriers upload scans only under their own ID;
- a read-only partner route with an API key;
- HTTPS everywhere and HSTS outside Development;
- an explanation of OAuth2 for production.

There is no identity provider in the course environment.

## Decision

- **JWT bearer, from configuration.** `AddJwtBearer` validates issuer, audience, lifetime (with one minute of skew) and signature, from `Authentication:Schemes:Bearer`. That is the section `dotnet user-jwts` fills in Development; elsewhere an identity provider's `Authority` replaces the issuer and keys. The API refuses to start without them. A Development-only `POST /dev/token` issues one-hour tokens for the courier app and the `.http` file, and answers 404 in every other environment.
- **Secure by default.** A fallback authorization policy requires an authenticated caller for every endpoint, including a future one that nobody remembered to protect. Only the probe, the documents and Swagger UI (Development only) and the partner route opt out, each explicitly.
- **Roles on the actions, the same in v1 and v2.** Admin creates, replaces and deletes shipments; Courier changes status and uploads scans; reads need any valid token. A test pins the rule of every action.
- **The courierId rule is an action filter** (`RequireOwnCourierIdAttribute`), because it needs the request body, which authorization runs before. It runs after model validation (a malformed body is still a 400) and before the action, so a refused upload stores nothing.
- **401 and 403 are ProblemDetails.** A 401 carries the RFC 6750 `WWW-Authenticate` header and says what to do without naming the failed check. A 403 names the missing role, which the documents already state.
- **The partner route takes an `X-Api-Key`** checked by an endpoint filter attached only to actions marked `[RequirePartnerApiKey]`, which go with `[AllowAnonymous]`. A test keeps the two together.
  - **Storage:** keys come from configuration (user-secrets in Development) and are kept as SHA-256 hashes. A presented key is compared with every hash in constant time.
  - **Never shown:** keys are never logged and never in error messages.
  - **Validated at start-up:** at least 32 characters, unique, with a partner name.
  - **Minimal response:** status, city and the latest scan's type and time. No recipient details and no scan locations.
- **HTTPS.** HTTP is redirected in every environment (308 outside Development), and HSTS is sent for a year outside Development. The clients refuse to send a token over plain HTTP to anything but this machine, and no code turns certificate validation off.

## Consequences

- **Moving to a real identity provider is configuration, not code:** set `Authority`, register the clients (the courier app with Authorization Code + PKCE, partners with Client Credentials), and delete `/dev/token`. SECURITY.md describes the flows and why the API keys would then be retired.
- **The changes break old callers on purpose, in every version.** VERSIONING.md records why security is not versioned: an old version without the checks would be the way around them.
- **The test hosts make up their own signing key and partner keys each run,** so no developer's secret is ever used by a test. Theory data names its cases instead of carrying those random values, because Visual Studio's runner enumerates and runs tests in different processes.
- **API keys remain the weakest credential here:** long-lived, shared and revoked by hand. The hashing, the constant-time comparison, the logging rules, the start-up checks, rotation with two keys and a read-only, minimal route reduce the risk but do not remove it.
