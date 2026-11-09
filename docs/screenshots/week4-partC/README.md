# Week 4 Parts B and C screenshots

Taken from Swagger UI on a database with the tracking tables, with fictional data only:

```powershell
dotnet ef database update --project src/ApexPost.Infrastructure      # once: adds TrackedShipments and TrackingScans
dotnet run --project src/ApexPost.Api --launch-profile http          # opens http://localhost:5245/swagger
```

Swagger UI opens on **v2**. For each request: open the operation, choose **Try it out**, edit the example if the table says so, then **Execute**. The response, with its status and headers, appears under the button.

| File | What it shows | Steps in Swagger UI |
|---|---|---|
| `01-swagger-v2.png` | The v2 document: title, version 2.0, the Shipments and Scans operations, with their operation IDs | The page as it opens; the dropdown at the top right reads "v2" |
| `02-swagger-v1-deprecated.png` | The v1 document: the deprecation notice in the description and every operation struck through | Choose "v1 (deprecated)" in the dropdown |
| `03-201-created-location.png` | A 201 with its `Location` header (`/api/v2/shipments/APX-24000001`) and the v2 body: nested `recipient`, `"status": "Created"`, `"latestScan": null` | v2, `POST /api/v2/shipments` (CreateShipment). Execute the example as it is |
| `04-409-conflict.png` | A 409 ProblemDetails: *"A shipment with the tracking number APX-24000001 already exists."* | Execute the same CreateShipment example a second time |
| `05-400-validation-problem-details.png` | A 400 ValidationProblemDetails, with errors keyed by JSON field | CreateShipment with `"trackingNumber": "APX-123"`, `"recipientPhone": "12"` and `"weightKg": 70.5` |
| `06-v1-deprecation-headers.png` (optional) | A v1 response with its `Deprecation`, `Sunset` and `Link` headers, and the flat v1 body with `"status": 0` | "v1 (deprecated)", `GET /api/v1/shipments/{trackingNumber}` with `APX-24000001` |

On a database that already has `APX-24000001`, use another `APX-` number with eight digits in steps 3 and 4.
