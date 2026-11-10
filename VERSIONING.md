# ApexPost tracking API versions

This file records every change to the tracking API's contract. For each change it says whether the change breaks clients, and why. It also gives v1's deprecation and sunset plan. The contracts themselves are committed as [`contracts/v1.json`](contracts/v1.json) and [`contracts/v2.json`](contracts/v2.json).

## How the API is versioned

- **The version is in the URL:** `/api/v1/shipments`, `/api/v2/shipments`. Only major versions exist. `/api/v2.0/…` is accepted as the same version; any other number, such as `/api/v3/…`, is a 404 ProblemDetails.
- **A new major version is made only for a breaking change**, and the old version keeps working unchanged until its published sunset date. A non-breaking change goes into the current version without a new number.
- **Both versions run the same code below the controllers.** They call the same `ShipmentTrackingService`, on the same data, with the same rules. Only the response DTOs and their mapping differ (`Contracts` for v1, `Contracts/V2` for v2), so a fix or a business rule applies to every version at once.
- **Every response says which versions exist:** `api-supported-versions: 2.0` and `api-deprecated-versions: 1.0`. A deprecated version's successful responses also carry its dates:

  ```http
  Deprecation: @1794182400
  Sunset: Mon, 31 May 2027 00:00:00 GMT
  Link: <https://developer.apexpost.example/api/versioning>; rel="sunset"; title="ApexPost API versions: moving from v1 to v2"; type="text/html",<https://developer.apexpost.example/api/versioning>; rel="deprecation"; title="ApexPost API versions: moving from v1 to v2"; type="text/html"
  ```

  `Deprecation` follows RFC 9745 (Unix time: 9 November 2026), and `Sunset` follows RFC 8594. The policy page is fictional, like all ApexPost addresses.
- **Each version has its own OpenAPI document,** `/openapi/v1.json` and `/openapi/v2.json`. The v1 document opens with the deprecation notice, and every v1 operation has `deprecated: true`. Swagger UI lists "v2" first and "v1 (deprecated)" second.

### What counts as breaking

A change is **breaking** when a client written against the old contract can fail or misread data without changing its own code:
- a field is removed, renamed or moved;
- a field's type or format changes;
- a field that was never null can now be null;
- a request gains a required field or a stricter rule;
- a status code changes for the same situation;
- a URL changes.

A change is **non-breaking** when such a client keeps working:
- a new optional request field, a new endpoint, a new response field, or a looser rule;
- a documentation-only change.

A new response field counts as non-breaking because clients are expected to ignore fields they do not know. This is how System.Text.Json and the generated client behave by default.

## Versions

| Version | Released | Status | Contract |
|---|---|---|---|
| v2 | 9 November 2026 (Week 4, Part C) | **Current** | [`contracts/v2.json`](contracts/v2.json) |
| v1 | Part B, as `/api/shipments`; renamed `/api/v1` on 9 November 2026 | **Deprecated** since 9 November 2026; **sunset** 31 May 2027 | [`contracts/v1.json`](contracts/v1.json) |

## Changes from v1 to v2

| # | Change | Breaking? | Why |
|---|---|---|---|
| 1 | The recipient fields move into a nested object: `recipientName`, `recipientPhone`, `recipientAddress`, `city` become `recipient { name, phone, address, city }` (shipment, list item) | **Breaking** | The four top-level fields disappear. A v1 client reading `recipientName` would find nothing, or fail if it requires the field. |
| 2 | `status` is written as a name: `"status": "InTransit"` instead of `2` (shipment, list item) | **Breaking** | The JSON type changes from number to string. A client deserialising into an integer or a numeric enum fails. |
| 3 | `scanType` in the scan history is written as a name: `"Pickup"` instead of `0` | **Breaking** | Same reason as change 2 |
| 4 | `shipmentStatus` in the scan upload result is written as a name | **Breaking** | Same reason as change 2 |
| 5 | A shipment gains `latestScan { scanType, location, scannedAtUtc }`, which is `null` until the first scan | Non-breaking on its own | A new field that clients ignore if they do not know it. It came with v2 because it belongs to v2's new shipment shape; on its own it could have been added to v1. |
| 6 | The requests do not change: create, replace, status change and scan upload take the same bodies and query parameters, with enums as names or numbers | Non-breaking (no change) | A v1 client's requests work unchanged against v2. Only its reading of responses has to change. |
| 7 | Errors do not change: the same status codes and the same ProblemDetails bodies | Non-breaking (no change) | |
| 8 | v2 adds a read-only partner route, `GET /api/v2/partner/shipments/{trackingNumber}`, which takes an `X-Api-Key` header (Part D) | Non-breaking | A new operation; nothing a v1 client calls changes. It is not added to v1, because v1 is deprecated and gets no new features. |

### Changes inside v1 since Part B

None of these changes the bytes of a v1 response, so they are not breaking. v1 stays the Part B contract.

| Change | Breaking? | Why |
|---|---|---|
| The URL gains its version: `/api/shipments` becomes `/api/v1/shipments` | Non-breaking, until v1's sunset | The Part B URLs keep answering as an alias of v1, with v1's headers. A `Location` header gives the new URL. The alias is removed together with v1. |
| Responses add the `api-supported-versions`, `api-deprecated-versions`, `Deprecation`, `Sunset` and `Link` headers | Non-breaking | They are new headers; the bodies are unchanged. |
| The OpenAPI document was added and then made more precise (Part C):<br>• descriptions and examples;<br>• every status code;<br>• enums named in requests;<br>• numbers typed as numbers, with the weight as a decimal;<br>• required request fields not nullable;<br>• no host-specific `servers` list;<br>• operations marked deprecated | Non-breaking | Documentation only: it now describes what the API already did. |

### Security changes in every version (Part D)

These apply to **v1 and v2 alike**, and to the unversioned Part B URLs. Security is not versioned: an old version that kept accepting anonymous callers would be the way around every check.

| Change | Breaking? | Why |
|---|---|---|
| Every endpoint requires a JWT bearer token. Without a valid one the answer is **401** ProblemDetails with `WWW-Authenticate`. Both documents declare the `Bearer` scheme and a 401 on every operation ([SECURITY.md](SECURITY.md)) | **Breaking**, made without a new version | A client that sent no token now gets 401. It is not versioned because anonymous access was never meant to last: until now the README said to run the API only locally, and that authentication would follow in Part D. |
| Roles: creating, replacing and deleting shipments need **Admin**; status changes and scan uploads need **Courier**; a courier uploads scans only under their own `courierId`. Otherwise the answer is **403** ProblemDetails, declared on those five operations | **Breaking**, made without a new version | A token without the role now gets 403 where it got 200. The same reason applies: a rule that only one version enforced would protect nothing. |
| The `Bearer` and `PartnerApiKey` scheme descriptions put `Authorization: Bearer <token>` and `X-Api-Key: <key>` in code spans (10 November 2026) | Non-breaking | Documentation only. Swagger UI had dropped `<token>` and `<key>` as unknown HTML tags, so the Authorize dialog read "sent as X-Api-Key: ." A test now refuses any description with an angle-bracket placeholder outside a code span. |

## Deprecation and sunset plan for v1

| When | What happens |
|---|---|
| **9 November 2026**: v2 released, v1 deprecated | v1 keeps answering exactly as before. Every successful v1 response carries `Deprecation`, `Sunset` and `Link` headers. The v1 OpenAPI document opens with the notice, and its operations are marked deprecated. Swagger UI labels it "v1 (deprecated)". The generated client ([`ApexPost.Client`](src/ApexPost.Client)) is built from v2 only. |
| **Until 31 May 2027** | v1 gets security and data-correctness fixes only. These come for free, because both versions share the service. v1 gets no new fields or endpoints; new work goes into v2. |
| **1 March 2027** (three months before sunset) | The platform team looks in the request logs for callers that still use `/api/v1` or the unversioned `/api/shipments`, and contacts each of them (contact: platform-team@apexpost.example). |
| **1 May 2027** (one month before) | A second notice goes to every caller still on v1. |
| **After 31 May 2027** | v1 is removed: `Controllers/V1`, the v1 response DTOs, the unversioned URL rewrite, and v1's deprecation and sunset policies. `/api/v1/…` and `/api/shipments…` then answer 404 like any unknown version. `contracts/v1.json` is deleted; it stays in the git history. |

### Moving a client from v1 to v2

1. Change the base path from `/api/v1/` (or `/api/`) to `/api/v2/`. Requests stay the same.
2. Read the recipient from `recipient.name`, `recipient.phone`, `recipient.address` and `recipient.city`.
3. Read `status`, `scanType` and `shipmentStatus` as names: `Created`, `PickedUp`, `InTransit`, `OutForDelivery`, `Delivered`, `Failed`; and `Pickup`, `InTransit`, `Delivered`, `Failed`.
4. Optionally, use `latestScan` instead of fetching the scan history to show where a parcel is.

A .NET client can use [`ApexPost.Client`](src/ApexPost.Client) instead. It is generated from `contracts/v2.json`, and `AddApexPostClient(baseAddress)` registers it.

## Changing the contract

Every change to a controller, a DTO or an XML comment that changes an OpenAPI document goes through these steps:

1. Run `./contracts/export-contracts.ps1`, which writes `contracts/v1.json` and `contracts/v2.json` from the API.
2. Review the diff of `contracts/*.json`. That diff *is* the contract change.
3. Classify it with the rules above:
   - **non-breaking:** it goes into the current version; add a row to this file;
   - **breaking:** it needs a new major version (v3), and v2 gets a deprecation and sunset plan like v1's.
4. Regenerate the client: `dotnet tool restore`, then `dotnet nswag run src/ApexPost.Client/nswag.json`.
5. Commit the code, the contracts, the client and this file together.

`ContractTests` fails when the API no longer matches `contracts/*.json`, so a contract change cannot be merged without step 1. `ClientGenerationTests` fails when the client lacks a method for an operation in `contracts/v2.json`.
