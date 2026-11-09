# ApexPost tracking API security

This file describes how `ApexPost.Api` decides who may call it: couriers and the back office with JWT bearer tokens and roles, partner companies with API keys, and everything over HTTPS. It ends with how OAuth2 would replace the development tokens and the API keys in production. The Week 3 counter application's security is in [docs/security.md](docs/security.md).

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

## Transport security: HTTPS and HSTS

**Why tokens and keys must never travel over plain HTTP.** A bearer token and an API key work for whoever holds them: the API cannot tell the courier's phone from someone who copied the header. Over plain HTTP the header crosses every network on the way in clear text: the café Wi-Fi, the mobile operator, a proxy, a compromised router. Anyone there can read it and replay it until the token expires, or for as long as the API key lives, with no password and nothing the API could detect. TLS encrypts the whole request, headers included, and the certificate proves the client is talking to the real API and not to someone in the middle. So the rule is HTTPS for every request that carries a credential, without exception.

**What the API does.**

| Where | Behaviour |
|---|---|
| Every environment | `UseHttpsRedirection()` sends a plain-HTTP request to its `https://` URL before authentication runs, so the API never answers with data over HTTP. Outside Development the redirect is **308**, which is permanent and keeps the method and body; in Development it is 307. It needs to know the HTTPS port: the `https` launch profile in Development, the deployment's `https_port` setting elsewhere. |
| Outside Development | `UseHsts()` adds `Strict-Transport-Security: max-age=31536000` (one year) to HTTPS responses. A browser that has seen it uses only HTTPS for this host from then on, even if a link or a user types `http://`, which defeats SSL-stripping. `includeSubDomains` and `preload` are left off: they bind every subdomain and cannot be undone quickly, a decision for whoever owns the domain. |
| Development | No HSTS. A browser would remember it for `localhost` for a year and refuse plain HTTP to every local project. |

Tests check the 308/307 redirect, the HSTS header outside Development and its absence in Development, and that an API request over HTTP gets only the redirect, never data.

**A redirect is a safety net, not the protection.** By the time the API redirects, the token has already crossed the network in clear text. HSTS helps browsers only; API clients such as the courier app and partner systems ignore it. So the real protections are these:
- **The server should not listen on plain HTTP at all.** In production, Kestrel or the load balancer exposes only HTTPS (TLS 1.2 or later), and HTTP requests are refused rather than redirected. The redirect then only serves people who type a URL by hand.
- **Clients refuse to send credentials over HTTP.** `ApexPost.Client`'s `BearerTokenHandler` throws instead of sending a token to any `http://` address other than this machine (`localhost`, `127.0.0.1`, `::1`, where nothing crosses a network). A test checks both sides of the rule.

**Certificates.** No code in this repository turns certificate validation off, and a test scans every source file for the calls that would. Locally, HTTPS uses the ASP.NET Core development certificate. Trust it once with `dotnet dev-certs https --trust` and the clients validate it like any other certificate, so no bypass is needed. If a machine ever needed one, it would be allowed only under `#if DEBUG`, only for `localhost`, and recorded here; there is none.

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

## Trying the API in Swagger UI

Both OpenAPI documents declare the `Bearer` scheme and require it on every operation; v2 also declares `PartnerApiKey`, required on the partner route only. Swagger UI therefore shows an **Authorize** button:
1. Start the API with `dotnet run --project src/ApexPost.Api --launch-profile https` and open `https://localhost:7245/swagger`.
2. Get a token: from `dotnet user-jwts create ...`, or from `POST /dev/token` with any HTTP client (it is not in the document).
3. Click **Authorize**, paste the token (without the word `Bearer`) under **Bearer**, and click **Authorize**. For the partner route, paste a partner key under **PartnerApiKey** in v2.
4. **Try it out** on any operation: Swagger UI sends `Authorization: Bearer <token>` (or `X-Api-Key`). Without authorizing, the same call answers 401; with a Courier-only token, deleting a shipment answers 403.

Swagger UI keeps the token in the page only and forgets it on reload. Persisting it in the browser's storage is deliberately not turned on.

## Clients

- **[`ApexPost.Client`](src/ApexPost.Client):** `AddApexPostClient(baseAddress).WithBearerToken(...)` adds a handler that sets `Authorization: Bearer` on every request. The token is fetched per request, so a renewed token takes effect at once. The handler never logs or stores it, and refuses to send it over plain HTTP to any host but this machine.
- **The console consumer:** uses `--ApexPostApi:AccessToken=<token>` (or the environment variable `ApexPostApi__AccessToken`). Without one, it asks a Development API's `/dev/token` and keeps the token in memory only.
- **[`ApexPost.Api.http`](src/ApexPost.Api/ApexPost.Api.http):** gets a token from `/dev/token` in its first request and sends it with every other request. The partner requests read the key from the environment variable `APEXPOST_PARTNER_API_KEY`, so it is never written into the file.

## OAuth2: how couriers and partners would get tokens in production

In Development, tokens come from `dotnet user-jwts` and `/dev/token`, which hand any role to anyone who asks. In production an **OAuth2** identity provider issues them. This section describes the design; nothing here is implemented yet, because the course runs without an identity provider.

### Who does what

| Part | Role in ApexPost |
|---|---|
| **Identity provider** (authorization server), for example Microsoft Entra ID, Auth0 or Keycloak | The only place a password or a client secret is checked. It knows the couriers and the registered partner systems, decides which roles and `courierId` each gets, and signs the tokens. The API never sees a password. |
| **Access token** (a JWT) | What the courier app or partner sends to the API as `Authorization: Bearer`. It is short-lived (minutes up to an hour), signed by the identity provider, and carries `iss`, `aud` (this API), `exp`, the roles and `courierId`. The API checks it on its own, offline, with the provider's public key; this API already does exactly that (see [Authentication](#authentication-jwt-bearer-tokens)). |
| **Refresh token** | A longer-lived credential that only the identity provider accepts, never the API. When the access token expires, the app sends the refresh token to the provider and gets a new access token, so the courier does not sign in every hour. The provider rotates it on every use (a reused old one revokes the whole chain), and an administrator can revoke it when a phone is lost or a courier leaves. It must be stored like a password: in the device's secure storage, never in plain files or logs. |
| **Resource server**: `ApexPost.Api` | Accepts or refuses access tokens; it issues nothing. |

The split matters: a stolen access token works only until it expires and only for this API, while the long-lived refresh token never travels to the API at all.

### Authorization Code with PKCE: the courier app

The courier app runs on a device the courier carries, so it is a **public client**: it cannot keep a client secret, because anything shipped in the app can be extracted. Authorization Code with PKCE (RFC 7636) is the flow for exactly this case:
1. The app makes a random **code verifier** and sends only its SHA-256 hash, the **code challenge**, when it opens the identity provider's sign-in page in the **system browser** (not an embedded web view, which the app could spy on).
2. The courier signs in on the provider's page, with MFA if required. The app never sees the password.
3. The provider redirects back to the app with a one-time **authorization code**. A console or desktop app receives it on a loopback address such as `http://127.0.0.1:<port>/callback` (RFC 8252); a phone app on its registered app link.
4. The app exchanges the code **and the original code verifier** for an access token and a refresh token. The provider hashes the verifier and checks it against the challenge from step 1, so an attacker who intercepted the code on the way back cannot use it: they never had the verifier.
5. The app sends the access token to the API, and uses the refresh token to renew it.

For a courier device without a usable browser, the **device authorization grant** (RFC 8628) is the alternative: the app shows a short code, and the courier confirms it on their phone.

### Client Credentials: partner systems

A partner's tracking system is a server talking to a server, with no person signing in. It is a **confidential client**: it can keep a secret on its own servers. With the Client Credentials flow, the partner system authenticates to the identity provider with its client ID and a secret, or better a certificate or signed assertion (`private_key_jwt`), and receives a short-lived access token. That token's audience is this API, and its scope or role allows only the partner route. It then calls the API with `Authorization: Bearer`, exactly as couriers do. There is no refresh token: the partner simply asks for a new access token when the old one expires.

### Why API keys are weaker than OAuth2 tokens

The partner route uses API keys today because the brief asks for them and they are simple to adopt. They are the weaker choice:

| | API key | OAuth2 access token |
|---|---|---|
| **Lifetime** | Valid until someone rotates it, often for years. A leaked key works for that long. | Expires in minutes to an hour. A leaked token soon stops working. |
| **What crosses the network** | The long-lived secret itself, on every request. | A short-lived token. The long-lived secret goes only to the identity provider, and with a certificate not even that. |
| **Who holds the secret** | Both sides: the API must store something that can check it, so a leak on either side exposes it. | The API holds only the provider's public key and can verify tokens but never mint them. |
| **What it says** | Only "this is partner X". There is no expiry, audience, scope or user in it. | Signed claims: who, for which API, which roles or scopes, until when. |
| **Revocation and rotation** | Manual, through configuration and a redeploy, coordinated with the partner. | Central at the identity provider: disable the client and no new tokens are issued; secrets and keys can be rotated without touching the API. |
| **Standards and tooling** | Every API invents its own header and format. | One standard flow, with libraries, auditing and MFA for people. |

This API reduces the risk of its keys: only hashes are kept, comparison is in constant time, keys are never logged, HTTPS only, a minimum length, rotation with two keys, a read-only route, and a reduced response. Moving partners to Client Credentials would remove the keys entirely.

## Using a real identity provider

The development tokens stand in for an identity provider. Moving to one changes configuration and the clients, not the API's checks:
1. **Register the API** with the identity provider. It defines the audience, the `Courier` and `Admin` roles, and the `courierId` claim, for example from the courier's directory record.
2. **Replace the issuer and keys with `Authority`.** Remove `ValidIssuer` and `SigningKeys`, and set `Authority` to the identity provider's URL. `AddJwtBearer` then reads its issuer and **public** signing keys from the provider's OpenID Connect metadata. It follows key rotation by itself, and the API holds no secret at all. The start-up check already accepts this.
3. **Map the claims** if the provider names them differently, for example `roles` instead of `role` (`TokenValidationParameters.RoleClaimType`).
4. **Register the clients:** the courier app as a public client using Authorization Code with PKCE, and each partner system as a confidential client using Client Credentials. The partner route then accepts bearer tokens with a partner scope, and the API keys are retired.
5. **Delete `POST /dev/token`** (`DevelopmentTokenEndpoint`), and stop using `dotnet user-jwts` outside local development.

The checks, roles, claims, and the 401 and 403 responses stay exactly as described above.
