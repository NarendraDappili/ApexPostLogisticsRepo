# Week 4 Parts B and C screenshots

Taken from Swagger UI and the console on a database with the tracking tables, with fictional data only (see [the index](../README.md) for how). Part B's other status codes are in [week4-partB](../week4-partB/README.md).

```powershell
dotnet ef database update --project src/ApexPost.Infrastructure      # once: adds TrackedShipments and TrackingScans
dotnet run --project src/ApexPost.Api --launch-profile https         # opens https://localhost:7245/swagger
```

**Since Part D every operation needs a token.** Before the first request, get one and click **Authorize** in Swagger UI ([SECURITY.md](../../../SECURITY.md#trying-the-api-in-swagger-ui)):

```powershell
dotnet user-jwts create --project src/ApexPost.Api --role Admin --role Courier --claim courierId=CR-1042     # prints a token
```

Paste the token under **Bearer** and click **Authorize**; Swagger UI then sends it with every request.

Swagger UI opens on **v2**. For each request: open the operation, choose **Try it out**, edit the example if the table says so, then **Execute**. The response, with its status and headers, appears under the button.

| File | What it shows | Steps |
|---|---|---|
| [`01-swagger-v2.png`](01-swagger-v2.png) | The v2 document: title "ApexPost Logistics API", version 2.0, the description and contact, the Authorize button, and the Shipments, Scans and Partner operations with their operation IDs | The page as it opens; the dropdown at the top right reads "v2" |
| [`02-swagger-v1-deprecated.png`](02-swagger-v1-deprecated.png) | The v1 document: the deprecation notice in the description and every operation struck through | Choose "v1 (deprecated)" in the dropdown |
| [`03-201-created-location.png`](03-201-created-location.png) | A 201 with its `Location` header (`/api/v2/shipments/APX-24000001`) and the v2 body: nested `recipient`, `"status": "Created"`, `"latestScan": null` | v2, `POST /api/v2/shipments` (CreateShipment). Execute the example as it is |
| [`04-409-conflict.png`](04-409-conflict.png) | A 409 ProblemDetails: *"A shipment with the tracking number APX-24000001 already exists."* | Execute the same CreateShipment example a second time |
| [`05-400-validation-problem-details.png`](05-400-validation-problem-details.png) | A 400 ValidationProblemDetails, with errors keyed by JSON field | CreateShipment with `"trackingNumber": "APX-123"`, `"recipientPhone": "12"` and `"weightKg": 70.5` |
| [`06-v1-deprecation-headers.png`](06-v1-deprecation-headers.png) | A v1 response with its `Deprecation`, `Sunset` and `Link` headers, and the flat v1 body with `"status": 0` | "v1 (deprecated)", `GET /api/v1/shipments/{trackingNumber}` with `APX-24000001` |
| [`07-operation-metadata.png`](07-operation-metadata.png) | Rich metadata on one operation: the `[EndpointSummary]`, the XML-comment description, the operation ID (`UploadScans`), the parameter and body descriptions with an example, and every documented response code with its meaning | v2: open `POST /api/v2/shipments/{trackingNumber}/scans` without Try it out |
| [`08-client-consumer.png`](08-client-consumer.png) | The console consumer of the generated client: creates a shipment, fetches it, uploads two scans, fetches it again, and catches a 404 as `ApexPostApiException<ProblemDetails>` | With the API running: `dotnet run --project src/ApexPost.Client.Consumer -- --ApexPostApi:BaseAddress=https://localhost:7245/` |
| [`09-nswag-regenerate-no-diff.png`](09-nswag-regenerate-no-diff.png) | The committed NSwag command regenerates the client from `contracts/v2.json`, and the result has the same Git blob hash as the committed file | `dotnet nswag run src/ApexPost.Client/nswag.json`, then compare `git rev-parse HEAD:<file>` with `git hash-object --no-filters <file>` |
| [`10-contract-drift-tests.png`](10-contract-drift-tests.png) | The contract tests: the committed `contracts/v1.json` and `v2.json` are exactly what the API serves, and they differ only where VERSIONING.md says | `dotnet test tests/ApexPost.Api.Tests --filter "FullyQualifiedName~ContractTests" --logger "console;verbosity=normal"` |

On a database that already has `APX-24000001`, use another `APX-` number with eight digits in steps 3 and 4.

After `dotnet nswag run` on Windows, `git status` may list the generated file as modified even though its content is unchanged: NSwag writes LF line endings, and `core.autocrlf=true` expects CRLF in the working copy. `git checkout -- src/ApexPost.Client/Generated/ApexPostClient.g.cs` clears it.
