# Week 4 screenshots

Every Week 4 deliverable that a picture can show, and the file that shows it. All data is fictional.

**How they were taken (10 November 2026).** Every picture comes from a real run of the code in this repository. The Release build ran against a throwaway SQL Server database (`ApexPostDb_Screens`, created from the migrations and dropped afterwards), with a throwaway partner key.
- **Swagger UI:** captured in Microsoft Edge (headless), driven by Playwright: open the operation, choose **Try it out**, **Execute**. Each picture runs from the operation's header to the end of the server's response. The `curl` box is hidden because it repeats the token or API key.
- **Console programs** (the harness, the courier app, the consumer, `curl.exe`, NSwag, `dotnet test`): run with their input typed in by a script. The output is drawn in a terminal-style frame, with what was typed shown after each prompt, as a terminal echoes it. Nothing is edited. The courier-app pictures that need the API stopped or restarted say so in their first line.

Each part's folder has a README with the steps to take every picture again by hand.

## Part A: offline store ([week4-partA](week4-partA/README.md))

| Brief item | Picture |
|---|---|
| The change listener logs the IDs of changed documents and shows a live pending count | [01](week4-partA/01-sample-data-listener-log.png), [05](week4-partA/05-sync-online-listener-log.png) |
| SQL++: all scans for a tracking number, oldest first | [02](week4-partA/02-find-by-tracking-number.png) |
| A new scan is saved locally first, as Pending; SQL++: all Pending scans | [03](week4-partA/03-add-scan-and-list-pending.png) |
| The outbox with no network: scans stay Pending | [04](week4-partA/04-sync-offline.png) |
| The outbox online: scans marked Synced, and a rejected scan marked Failed with its reason | [05](week4-partA/05-sync-online-listener-log.png) |
| SQL++: scans per type for one courier (header); delete, and the listener after it | [06](week4-partA/06-scans-by-type-and-delete.png) |
| An embedded database belongs to one process | [07](week4-partA/07-second-process-refused.png) |

## Part B: REST API ([week4-partB](week4-partB/README.md))

| Brief item | Picture |
|---|---|
| `GET /shipments` paged and filtered (`page`, `pageSize`, `city`) | [01](week4-partB/01-list-paged-filtered-200.png) |
| 404 ProblemDetails for an unknown tracking number | [02](week4-partB/02-get-unknown-404.png) |
| `PUT`: replace the editable details, 200 | [03](week4-partB/03-put-replace-200.png) |
| `PATCH …/status`: 200, then 409 for a transition the rules do not allow | [04](week4-partB/04-patch-status-200.png), [05](week4-partB/05-patch-invalid-transition-409.png) |
| `DELETE`: 409 when not Created, 204 when Created | [06](week4-partB/06-delete-not-created-409.png), [07](week4-partB/07-delete-created-204.png) |
| Idempotent scan upload: `accepted`, `duplicatesSkipped`, `rejected` with reasons | [08](week4-partB/08-scan-upload-accepted-and-rejected.png), [09](week4-partB/09-scan-reupload-duplicates-skipped.png) |
| A custom validation rule (a scan time cannot be in the future), as a 400 ValidationProblemDetails | [10](week4-partB/10-400-scan-time-in-future.png) |
| `GET …/scans`: scan history, oldest first | [11](week4-partB/11-scan-history-200.png) |
| 415 for a wrong content type, as ProblemDetails | [12](week4-partB/12-415-unsupported-media-type.png) |
| 201 with `Location`, 409 duplicate, 400 ValidationProblemDetails | Part C [03](week4-partC/03-201-created-location.png), [04](week4-partC/04-409-conflict.png), [05](week4-partC/05-400-validation-problem-details.png) |

## Part C: OpenAPI, versioning and the generated client ([week4-partC](week4-partC/README.md))

| Brief item | Picture |
|---|---|
| **Swagger UI showing v2** (title, version, contact, tags, operation IDs) | [01](week4-partC/01-swagger-v2.png) |
| **Swagger UI showing v1**, labelled "v1 (deprecated)", every operation struck through | [02](week4-partC/02-swagger-v1-deprecated.png) |
| **A 201 response with its `Location` header** | [03](week4-partC/03-201-created-location.png) |
| **A 409 response** | [04](week4-partC/04-409-conflict.png) |
| **A 400 ValidationProblemDetails response** | [05](week4-partC/05-400-validation-problem-details.png) |
| v1's `Deprecation`, `Sunset` and `Link` headers and the flat v1 body | [06](week4-partC/06-v1-deprecation-headers.png) |
| Rich metadata: summary, XML-comment description, operation ID, every response code | [07](week4-partC/07-operation-metadata.png) |
| The console consumer: create, fetch, upload two scans, the 404 through the generated exception | [08](week4-partC/08-client-consumer.png) |
| The client regenerated from `contracts/v2.json` is identical to the committed one | [09](week4-partC/09-nswag-regenerate-no-diff.png) |
| Contract tracking: the committed contracts match what the API serves | [10](week4-partC/10-contract-drift-tests.png) |

## Part D: security and the courier app ([week4-partD](week4-partD/README.md))

| Brief item | Picture |
|---|---|
| Bearer and API key schemes in the v2 document; the Swagger UI **Authorize** button | [01](week4-partD/01-swagger-authorize.png) |
| **401** without a token, as ProblemDetails with `WWW-Authenticate: Bearer` | [02](week4-partD/02-401-no-token.png) |
| **403** for the wrong role, as ProblemDetails naming the role | [03](week4-partD/03-403-courier-cannot-delete.png) |
| **403** for scans under another courier's `courierId` | [04](week4-partD/04-403-other-courier-id.png) |
| Partner route: **401** for an invalid API key, **200** for a valid one (no recipient details) | [05](week4-partD/05-partner-401-invalid-key.png), [06](week4-partD/06-partner-200-valid-key.png) |
| **Login** screen: a token from the server, kept in memory, with its expiry in the header | [07](week4-partD/07-courier-login.png) |
| **New Scan** and **Pending Scans** (with the count) while the API is down: scans are saved on the device | [08](week4-partD/08-courier-scans-saved-while-api-down.png) |
| Retries logged, then the **circuit breaker opens**: the "Server unavailable – working offline" banner; **Sync Status** with the circuit state and failures | [09](week4-partD/09-courier-retries-circuit-open.png) |
| The API is back: circuit half-open, then closed; **automatic sync** uploads the Pending scans in the background | [10](week4-partD/10-courier-recovery-auto-sync.png) |
| A whole delivery from the app: Out for Delivery, then the Delivered scan, synced | [11](week4-partD/11-courier-out-for-delivery-and-delivered.png) |
| A **401** clears the token and returns to **Login**; the scan stays Pending and is uploaded after signing in again | [12](week4-partD/12-courier-401-back-to-login.png) |
| HTTPS redirection | [13](week4-partD/13-http-redirects-to-https.png) |

## Not in a picture

| Item | Where it is shown instead |
|---|---|
| **The demo video** (login, scans in airplane mode, automatic sync after reconnecting, 401 and 403, the circuit breaker opening and recovering) | To be recorded with the script in [week4-partD/README.md](week4-partD/README.md). Turning off this PC's network would also cut the session that took these pictures, so the "network lost" and "network is back" lines are for the video. Pictures 08–10 show the same offline saving and recovery with the API stopped instead. |
| HSTS and the 308 redirect outside Development | `TransportSecurityTests` (the API hosted as Production). In Development the redirect is 307 and there is no HSTS, as picture D13 shows. |
| A 500 without a stack trace outside Development | `ErrorHandlingTests` in Production and Development ([API-NOTES.md](../../API-NOTES.md)) |
