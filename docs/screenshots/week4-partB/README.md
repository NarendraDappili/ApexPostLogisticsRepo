# Week 4 Part B screenshots

Every endpoint and status code of the tracking API, taken from Swagger UI and `curl.exe` on a database with the tracking tables, with fictional data only (see [the index](../README.md) for how). The 201, 409 and 400 of `POST /shipments` are in [Part C](../week4-partC/README.md), as the brief lists them there.

## Before the first picture

```powershell
dotnet ef database update --project src/ApexPost.Infrastructure                    # once: adds TrackedShipments and TrackingScans
dotnet run --project src/ApexPost.Api --launch-profile https                        # https://localhost:7245/swagger
```

Every operation needs a token. Get one with both roles, then click **Authorize** in Swagger UI, paste it under **Bearer**, and click **Authorize** again:

```powershell
dotnet user-jwts create --project src/ApexPost.Api --role Admin --role Courier --claim courierId=CR-1042
```

The pictures use these fictional shipments, created with `POST /api/v2/shipments` (CreateShipment) before the first picture. Each one has recipient phone `+44 20 7946 0000`, address `12 Harbour Road` and weight 2.5:

| Tracking number | City | Used in |
|---|---|---|
| `APX-24000011`, `APX-24000012`, `APX-24000013` | Portside | 01, 03 |
| `APX-24000014` | Lakeview | 01 (filtered out) |
| `APX-24000020` | Lakeview | 04, 05, 06 |
| `APX-24000030` | Riverside | 07 |
| `APX-24000040` | Portside | 08–11 |

## The pictures

For each one, open the operation in Swagger UI (v2), choose **Try it out**, enter the values, then **Execute**.

| File | What it shows | Operation and values |
|---|---|---|
| [`01-list-paged-filtered-200.png`](01-list-paged-filtered-200.png) | 200: page 1 of the Portside shipments, two per page, with `page`, `pageSize`, `totalCount` and `totalPages` | ListShipments: `page` 1, `pageSize` 2, `city` Portside |
| [`02-get-unknown-404.png`](02-get-unknown-404.png) | 404 ProblemDetails for an unknown tracking number | GetShipment: `APX-99999999` |
| [`03-put-replace-200.png`](03-put-replace-200.png) | 200: the editable details replaced | UpdateShipment: `APX-24000011`, a new phone, address and weight |
| [`04-patch-status-200.png`](04-patch-status-200.png) | 200: Created → PickedUp | UpdateShipmentStatus: `APX-24000020`, `{"status": "PickedUp"}` |
| [`05-patch-invalid-transition-409.png`](05-patch-invalid-transition-409.png) | 409: PickedUp → Delivered is not an allowed transition | UpdateShipmentStatus: `APX-24000020`, `{"status": "Delivered"}` |
| [`06-delete-not-created-409.png`](06-delete-not-created-409.png) | 409: a shipment can be deleted only while Created | DeleteShipment: `APX-24000020` |
| [`07-delete-created-204.png`](07-delete-created-204.png) | 204 No Content | DeleteShipment: `APX-24000030` |
| [`08-scan-upload-accepted-and-rejected.png`](08-scan-upload-accepted-and-rejected.png) | 200: `accepted` 2, `rejected` 1 with its reason (a Delivered scan needs OutForDelivery first), and the new status | UploadScans: `APX-24000040`, three scans by CR-1042: Pickup, InTransit, Delivered, each with a new `scan::` ID and a past time |
| [`09-scan-reupload-duplicates-skipped.png`](09-scan-reupload-duplicates-skipped.png) | 200: the same upload again stores nothing twice: `accepted` 0, `duplicatesSkipped` 2 | Execute the same body again |
| [`10-400-scan-time-in-future.png`](10-400-scan-time-in-future.png) | 400 ValidationProblemDetails from the custom rule: a scan time cannot be in the future | UploadScans: one scan with `scannedAtUtc` an hour ahead |
| [`11-scan-history-200.png`](11-scan-history-200.png) | 200: the scan history, oldest first | GetScanHistory: `APX-24000040` |
| [`12-415-unsupported-media-type.png`](12-415-unsupported-media-type.png) | 415 ProblemDetails for a `text/plain` body | In PowerShell: `$token = (Invoke-RestMethod -Method Post https://localhost:7245/dev/token -ContentType 'application/json' -Body '{"name":"Back office","roles":["Admin","Courier"],"courierId":"CR-1042"}').accessToken`, then `curl.exe -s -i --ssl-no-revoke -X POST https://localhost:7245/api/v2/shipments -H "Authorization: Bearer $token" -H "Content-Type: text/plain" --data "APX-24000099"` |

A 500 cannot be caused on purpose against a healthy API. `ErrorHandlingTests` shows that it is ProblemDetails, with no message or stack trace outside Development.
