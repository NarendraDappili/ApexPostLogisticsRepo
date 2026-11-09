# ApexPost tracking API security

This file describes how `ApexPost.Api` decides who may call it. The Week 3 counter application's security is in [docs/security.md](docs/security.md).

## Authentication: JWT bearer tokens

Every API request must carry an access token in its `Authorization` header:

```http
GET /api/v2/shipments/APX-24000001
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

The token is a JWT. `AddAuthentication().AddJwtBearer()` accepts it only if **all four** checks pass:

| Check | What must hold | Configuration (`Authentication:Schemes:Bearer`) |
|---|---|---|
| Issuer | `iss` is the expected issuer | `ValidIssuer` |
| Audience | `aud` names this API | `ValidAudiences` |
| Lifetime | `exp` is in the future and `nbf` in the past, with one minute of allowed clock drift. A token without `exp` is refused | (fixed in code) |
| Signature | Signed with a configured key. An unsigned token (`alg: none`) is refused | `SigningKeys` |

The values come from configuration, never from code:
- **In Development,** `dotnet user-jwts` writes the issuer and audiences to `appsettings.Development.json` and the signing key to user-secrets. The key is never committed.
- **Elsewhere,** they come from the deployment's configuration or, with a real identity provider, from its metadata (see [Using a real identity provider](#using-a-real-identity-provider)).

**The API refuses to start** without an issuer, an audience and a signing key (or, with a real identity provider, an `Authority` and an audience), just as it does without a connection string. The message says how to create them.

### Tokens carry a role and a courier ID

| Claim | Meaning |
|---|---|
| `role` | `Courier` (scans parcels and moves them along the status chain) or `Admin` (the back office). A token can carry both |
| `courierId` | The courier the token belongs to, for example `CR-1042` |
| `sub`, `name` | Who the token was issued to |

### Secure by default

A **fallback authorization policy** requires an authenticated caller for every endpoint that does not say otherwise. A new endpoint is therefore protected without anyone remembering to protect it. Only these are open:
- `/health`, the hosting platform's probe;
- the OpenAPI documents and Swagger UI, which exist in Development only;
- `POST /dev/token`, also Development only.

Without a token, even a path that does not exist answers 401, not 404, so an anonymous caller cannot discover which routes exist.

### 401 responses

A missing, invalid or expired token gets a **401 ProblemDetails**, with the `WWW-Authenticate` header that RFC 6750 defines:

| Token | `WWW-Authenticate` | `detail` |
|---|---|---|
| None | `Bearer` | Send an access token in the Authorization header: Authorization: Bearer &lt;token&gt;. |
| Expired | `Bearer error="invalid_token", error_description="The token expired"` | The access token has expired. Sign in again to get a new one. |
| Wrong key, issuer or audience; unsigned; malformed | `Bearer error="invalid_token", error_description="The token is not valid"` | The access token is not valid for this API. |

The response says what to do, never which check failed beyond "expired". That would only help someone trying to forge a token.

## Getting a token in Development

There are two ways; both sign with the same key, for the same issuer and audience.

**`dotnet user-jwts`** (the .NET tool). The first `create` also makes the signing key:

```powershell
dotnet user-jwts create --project src/ApexPost.Api --name "Kofi Boateng" --role Courier --claim courierId=CR-1042
dotnet user-jwts create --project src/ApexPost.Api --name "Back office" --role Admin
dotnet user-jwts list --project src/ApexPost.Api        # the tokens created so far
```

Each command prints a token valid for three months; `--valid-for 1h` makes a shorter one.

**`POST /dev/token`**, for the courier app's Login screen and the `.http` file. It issues a one-hour token:

```http
POST http://localhost:5245/dev/token
Content-Type: application/json

{ "name": "Kofi Boateng", "roles": ["Courier"], "courierId": "CR-1042" }
```

```json
{ "accessToken": "eyJhbGciOiJIUzI1NiIs...", "tokenType": "Bearer", "expiresAtUtc": "2026-11-09T13:44:30+00:00" }
```

`/dev/token` stands in for an identity provider, and has none of its checks. **Anyone who can reach it can get any role.** That is why it is mapped only when the environment is Development; a test checks that Production and Staging answer 404. It is not part of the API's contract, so it is in no OpenAPI document and no generated client.

`/dev/token` needs the signing key that `dotnet user-jwts create` makes, so run that command once on each development machine.

## Clients

- **[`ApexPost.Client`](src/ApexPost.Client):** `AddApexPostClient(baseAddress).WithBearerToken(...)` adds a handler that sets `Authorization: Bearer` on every request. The token is fetched per request, so a renewed token takes effect at once. The handler never logs or stores it.
- **The console consumer:** uses `--ApexPostApi:AccessToken=<token>` (or the environment variable `ApexPostApi__AccessToken`). Without one, it asks a Development API's `/dev/token` and keeps the token in memory only.
- **[`ApexPost.Api.http`](src/ApexPost.Api/ApexPost.Api.http):** gets a token from `/dev/token` in its first request and sends it with every other request.

## Using a real identity provider

The development tokens stand in for an identity provider such as Microsoft Entra ID or Auth0. Moving to one changes configuration, not the API's code:
1. **Register the API** with the identity provider. It defines the audience, the `Courier` and `Admin` roles, and the `courierId` claim, for example from the courier's directory record.
2. **Replace the issuer and keys with `Authority`.** Remove `ValidIssuer` and `SigningKeys`, and set `Authority` to the identity provider's URL. `AddJwtBearer` then reads its issuer and **public** signing keys from the provider's OpenID Connect metadata. It follows key rotation by itself, and the API holds no secret at all.
3. **Delete `POST /dev/token`** (`DevelopmentTokenEndpoint`), and stop using `dotnet user-jwts` outside local development.

The checks, roles, claims and 401 responses stay exactly as described above.
