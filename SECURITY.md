# ApexPost tracking API security

This file describes how `ApexPost.Api` decides who may call it: couriers and the back office with JWT bearer tokens and roles, partner companies with API keys. The Week 3 counter application's security is in [docs/security.md](docs/security.md).

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
- `POST /dev/token`, also Development only;
- the partner route, which needs no token because it needs a partner API key instead ([Partner API keys](#partner-api-keys)).

Without a token, even a path that does not exist answers 401, not 404, so an anonymous caller cannot discover which routes exist.

### 401 responses

A missing, invalid or expired token gets a **401 ProblemDetails**, with the `WWW-Authenticate` header that RFC 6750 defines:

| Token | `WWW-Authenticate` | `detail` |
|---|---|---|
| None | `Bearer` | Send an access token in the Authorization header: Authorization: Bearer &lt;token&gt;. |
| Expired | `Bearer error="invalid_token", error_description="The token expired"` | The access token has expired. Sign in again to get a new one. |
| Wrong key, issuer or audience; unsigned; malformed | `Bearer error="invalid_token", error_description="The token is not valid"` | The access token is not valid for this API. |

The response says what to do, never which check failed beyond "expired". That would only help someone trying to forge a token.

## Authorization: roles and the courier's own scans

Authentication says who the caller is; authorization says what they may do. Each action declares its rule with `[Authorize(Roles = ...)]`, the same in v1 and v2 (and the unversioned Part B URLs, which are v1):

| Endpoint | Rule |
|---|---|
| `GET` a shipment, the shipment list, a scan history | Any valid token |
| `POST` (create), `PUT` (replace) and `DELETE` a shipment | `Admin` |
| `PATCH …/status` | `Courier` |
| `POST …/scans` (upload) | `Courier`, and every scan's `courierId` must be the token's `courierId` claim |

Creating and replacing shipments are back-office work, so they need `Admin` as delete does; the brief leaves them open. A test pins this table for every action in both versions, and checks that exactly these five declare a 403 in the OpenAPI documents.

**The courier's own scans.** A token with the `Courier` role for `CR-1042` can upload scans for `CR-1042` only. One scan under any other ID refuses the whole upload with 403, and nothing is stored. IDs are compared exactly, so `cr-1042` is another courier. A `Courier` token without a `courierId` claim cannot upload at all. The check needs the request body, which the authorization middleware has not read, so it is an action filter (`RequireOwnCourierIdAttribute`). It runs after a malformed body has been answered with 400, and before the shipment is looked up, so it answers 403 even for a shipment that does not exist.

### 403 responses

A valid token that may not do what it asks gets a **403 ProblemDetails** with the title "Access is denied." and no `WWW-Authenticate` header: signing in again would not help.

| Why | `detail` |
|---|---|
| The token lacks the role | This action needs the Admin role, which the access token does not have. (or Courier) |
| Scans under another courier | A courier can upload scans only under their own courierId, CR-1042. This upload has scans under CR-2001. |
| A courier token without `courierId` | The access token has no courierId claim, so it cannot upload scans. |

Naming the missing role gives nothing away: the OpenAPI documents say the same.

## Partner API keys

Partner companies (fictional ones here, such as "Northwind Freight") follow parcels through one read-only route, in v2 only:

```http
GET /api/v2/partner/shipments/APX-24000001
X-Api-Key: <the key ApexPost issued to the partner>
```

It answers the status, the delivery city and what happened at the latest scan, and when. It does not return the recipient's name, phone or address, or any scan location, because a delivery scan's location is the delivery address. A partner needs to know where the parcel is in its journey, not who receives it.

**How the key is checked.**
- **An endpoint filter, not a token.** The action is `[AllowAnonymous]`, which lifts the bearer requirement, and `[RequirePartnerApiKey]`, which attaches the filter (`PartnerApiKeyFilter`) to that action only. A test checks that an action never has one attribute without the other. A bearer token does not open the route, and a partner key opens no other route.
- **Before anything else.** The filter runs before the action, so a request without a valid key learns nothing, not even whether the shipment exists.
- **The header only.** A key in the query string is ignored: URLs end up in server logs, proxies and browser histories. Two `X-Api-Key` headers are refused rather than guessed between.
- **Hashed and compared in constant time.** At start-up each configured key is hashed with SHA-256, and only the hashes are kept. A presented key is hashed and compared with every stored hash using `CryptographicOperations.FixedTimeEquals`, without stopping at the first match. Neither the time taken nor an early exit shows how close a guess was.
- **Never logged.** The log says which partner called, or that a request was refused because the key was missing or invalid. It never contains a presented or a stored key, and a test checks that.

**A missing or invalid key is a 401 ProblemDetails**, with the title "A partner API key is required." and `WWW-Authenticate: ApiKey header="X-Api-Key"`:

| Key | `detail` |
|---|---|
| None | Send your partner API key in the X-Api-Key header. |
| Not a partner's key | The API key in the X-Api-Key header is not valid. Check it with ApexPost partner support. |

**Where the keys live.** In the `PartnerApi:Keys` configuration section, never in the repository. In Development they go in user-secrets:

```powershell
# A key is random, never a word: 32 random bytes in Base64.
$bytes = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); $key = [Convert]::ToBase64String($bytes)
dotnet user-secrets set "PartnerApi:Keys:0:Partner" "Northwind Freight" --project src/ApexPost.Api
dotnet user-secrets set "PartnerApi:Keys:0:Key" $key --project src/ApexPost.Api
$env:APEXPOST_PARTNER_API_KEY = $key        # for ApexPost.Api.http, which reads it with {{$processEnv ...}}
```

Elsewhere they come from the deployment's secret store, for example a key vault or environment variables (`PartnerApi__Keys__0__Key`).
- **Checked at start-up.** The API refuses to start if an entry has no partner name, a key shorter than 32 characters, or the same key as another entry. The message names the setting but never shows the key. With no keys at all it starts, logs a warning, and refuses every partner request.
- **Rotation.** A partner may have two entries at once. Add the new key, give it to the partner, and remove the old one once they have switched.

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
- **[`ApexPost.Api.http`](src/ApexPost.Api/ApexPost.Api.http):** gets a token from `/dev/token` in its first request and sends it with every other request. The partner requests read the key from the environment variable `APEXPOST_PARTNER_API_KEY`, so it is never written into the file.

## Using a real identity provider

The development tokens stand in for an identity provider such as Microsoft Entra ID or Auth0. Moving to one changes configuration, not the API's code:
1. **Register the API** with the identity provider. It defines the audience, the `Courier` and `Admin` roles, and the `courierId` claim, for example from the courier's directory record.
2. **Replace the issuer and keys with `Authority`.** Remove `ValidIssuer` and `SigningKeys`, and set `Authority` to the identity provider's URL. `AddJwtBearer` then reads its issuer and **public** signing keys from the provider's OpenID Connect metadata. It follows key rotation by itself, and the API holds no secret at all.
3. **Delete `POST /dev/token`** (`DevelopmentTokenEndpoint`), and stop using `dotnet user-jwts` outside local development.

The checks, roles, claims and 401 responses stay exactly as described above.
