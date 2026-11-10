# Week 4 Part D: demo video and screenshots

The brief asks for a 3–5 minute video showing: **login**, **scans saved in airplane mode**, **automatic sync after reconnecting**, **401 and 403 responses**, and **the circuit breaker opening while the API is stopped, then recovering**. Below is a script that covers each one, in that order, on one Windows PC. All data is fictional.

## Before recording (once)

```powershell
dotnet ef database update --project src/ApexPost.Infrastructure            # the tracking tables
dotnet user-jwts create --project src/ApexPost.Api --role Admin             # the token signing key (already done on this PC)
dotnet dev-certs https --trust                                              # trust https://localhost:7245 (already done on this PC)
```

For the partner request in scene 4, a partner key ([SECURITY.md](../../../SECURITY.md#partner-api-keys)):

```powershell
$bytes = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); $key = [Convert]::ToBase64String($bytes)
dotnet user-secrets set "PartnerApi:Keys:0:Partner" "Northwind Freight" --project src/ApexPost.Api
dotnet user-secrets set "PartnerApi:Keys:0:Key" $key --project src/ApexPost.Api
[Environment]::SetEnvironmentVariable("APEXPOST_PARTNER_API_KEY", $key, "User")    # reopen VS Code / Visual Studio after this
```

## Set-up for the recording

Three windows side by side: **API** (terminal 1), **courier app** (terminal 2), and **VS Code or Visual Studio** with `src/ApexPost.Api/ApexPost.Api.http`.

**Terminal 1, the API:**

```powershell
dotnet run --project src/ApexPost.Api --launch-profile https
```

**A fresh shipment for the courier to scan** (any terminal; it must still be in Created status):

```powershell
$admin = (Invoke-RestMethod -Method Post https://localhost:7245/dev/token -ContentType 'application/json' -Body '{"name":"Back office","roles":["Admin"]}').accessToken
Invoke-RestMethod -Method Post https://localhost:7245/api/v2/shipments -Headers @{ Authorization = "Bearer $admin" } -ContentType 'application/json' -Body '{"trackingNumber":"APX-24500001","recipientName":"Ama Mensah","recipientPhone":"+44 20 7946 0000","recipientAddress":"12 Harbour Road","city":"Portside","weightKg":2.5}'
```

If `APX-24500001` already exists, use another `APX-` number with eight digits, here and in scene 2.

**Terminal 2, the courier app.** Use shorter waits than the defaults, so the recovery happens on camera:

```powershell
dotnet run --project src/ApexPost.Courier -- --Courier:DataDirectory=.\courier-demo-data --Courier:AutoSync:Interval=00:00:15 --ApexPostApi:Resilience:BreakDuration=00:00:15
```

## The script (about 4½ minutes)

| # | Time | Do | The viewer sees |
|---|---|---|---|
| 1 | 0:00–0:30 | **Login.** Type `CR-1042`, then press Enter at the token prompt | `Signed in as CR-1042. Scans upload to the server (sign-in valid until …)`; the header says `Uploads: on until …`. Say: the token is kept in memory only, never in a file or a log. |
| 2 | 0:30–1:30 | **Cut the network** (see the note under the table). Then **New Scan** twice for `APX-24500001`: type 1 (Pickup) at "Riverside Depot", then type 2 (InTransit) at "North Hub". Open **Pending Scans**. Press **Sync Now** | `Network lost: scans are saved on this device until it comes back`; each scan `Saved on this device as Pending`; the live count in the header goes to 2; Pending Scans lists both; Sync Now answers `No network: nothing was sent.` |
| 3 | 1:30–2:10 | **Restore the network.** Wait a few seconds; then open **Sync Status** | `Network is back: uploading Pending scans in 2 s`, then `Auto-sync (network back): 2 synced, 0 failed, 0 still pending`; the pending count in the header drops to 0; Sync Status shows the last sync and `closed: calls go to the server`. |
| 4 | 2:10–3:10 | **401 and 403** in the `.http` file. Send the first request ("Get a development token"), then, in the Authentication and Authorization sections: "No token", "An invalid token", the two role tokens, "A courier cannot delete", "An admin cannot change a status", "Courier CR-001 cannot upload a scan under CR-002". In the Partner section: "an invalid API key" and "a valid API key" | Each response pane: **401** ProblemDetails with `WWW-Authenticate: Bearer` (and `error="invalid_token"`); **403** ProblemDetails naming the missing role, and the courierId one; **401** for the partner key with `WWW-Authenticate: ApiKey header="X-Api-Key"`; **200** with the valid key, without the recipient's details. |
| 5 | 3:10–4:20 | **Out for Delivery, then the circuit breaker.** In the app, choose **Out for Delivery** for `APX-24500001`. Stop the API (Ctrl+C in terminal 1). Then **New Scan** (type 3, Delivered, at "12 Harbour Road") and **Sync Now**. Open **Sync Status**. Then start the API again (the same command) and wait up to 15 s | `APX-24500001 is out for delivery.`; then `Retry 1 of 3 … after HttpRequestException` and the next retries, then `Circuit opened for 15 s … working offline`; the header shows `*** Server unavailable – working offline ***`; the scan stays Pending (still saved); Sync Status says `open since …`. After the restart: `Circuit half-open`, `Circuit closed: the server answers again`, `Auto-sync (periodic): 1 synced`; the banner is gone. |
| 6 | 4:20–4:40 | **Sync Status** once more, then **Exit** | Last sync, server last reached, the circuit closed, and the failures of the outage listed. |

**Cutting the network in scenes 2 and 3.** Airplane mode turns off wireless radios only, so use it on a laptop with Wi-Fi. A wired PC or a virtual machine needs its adapter turned off instead; this PC's only active adapter is the wired `Ethernet0`. In an elevated PowerShell, run `Disable-NetAdapter -Name Ethernet0 -Confirm:$false` for scene 2 and `Enable-NetAdapter -Name Ethernet0` for scene 3. If you reach the PC by Remote Desktop over that adapter, this also cuts your session, so record at the PC itself or disconnect the virtual network cable from the VM's host.

The API on the same PC stays reachable through `localhost` either way: it is the app's own network check that makes it wait. The app counts a network as up only if an adapter has a default gateway. If "Network lost" does not appear, check with `Get-NetAdapter` which adapter is still up.

## Screenshots

Taken on 10 November 2026 from real runs (see [the index](../README.md) for how). Pictures 01–06 come from Swagger UI and 13 from `curl.exe`. Pictures 07–12 are one courier-app session, started as in "Set-up for the recording" above, while the API was stopped and started around it.

| File | What it shows | How to take it again |
|---|---|---|
| [`01-swagger-authorize.png`](01-swagger-authorize.png) | Swagger UI's **Authorize** dialog with the **PartnerApiKey** and **Bearer** schemes (v2) | Click **Authorize** |
| [`02-401-no-token.png`](02-401-no-token.png) | **401** ProblemDetails with `WWW-Authenticate: Bearer`, without a token | GetShipment before authorizing |
| [`03-403-courier-cannot-delete.png`](03-403-courier-cannot-delete.png) | **403** ProblemDetails naming the missing Admin role | Authorize with a Courier-only token (`dotnet user-jwts create --project src/ApexPost.Api --role Courier --claim courierId=CR-1042`), then DeleteShipment |
| [`04-403-other-courier-id.png`](04-403-other-courier-id.png) | **403**: courier CR-1042 cannot upload scans under CR-2000 | Same token, UploadScans with `"courierId": "CR-2000"` |
| [`05-partner-401-invalid-key.png`](05-partner-401-invalid-key.png) | **401** for a wrong partner key, with `WWW-Authenticate: ApiKey header="X-Api-Key"` | **Logout** of Bearer; authorize **PartnerApiKey** with any wrong value; GetPartnerShipment |
| [`06-partner-200-valid-key.png`](06-partner-200-valid-key.png) | **200** for a valid partner key, without the recipient's details | Authorize **PartnerApiKey** with the key from user-secrets |
| [`07-courier-login.png`](07-courier-login.png) | **Login**: courier ID, Enter for a token from the server; the header shows `Uploads: on until …` | Scene 1 |
| [`08-courier-scans-saved-while-api-down.png`](08-courier-scans-saved-while-api-down.png) | With the API stopped: two **New Scans** saved on the device as Pending, the live count at 2, and **Pending Scans** listing them | Stop the API (Ctrl+C), then New Scan twice and Pending Scans |
| [`09-courier-retries-circuit-open.png`](09-courier-retries-circuit-open.png) | **Sync Now**: each retry logged with its delay, the circuit opening, the `*** Server unavailable – working offline ***` banner, the scans still Pending, and **Sync Status** with the open circuit and the failures | Sync Now, then Sync Status |
| [`10-courier-recovery-auto-sync.png`](10-courier-recovery-auto-sync.png) | The API is back: circuit half-open, then closed; **automatic sync** uploads both scans without a key press; Sync Status shows the circuit closed | Start the API again and wait up to 15 s; then Sync Status |
| [`11-courier-out-for-delivery-and-delivered.png`](11-courier-out-for-delivery-and-delivered.png) | **Out for Delivery**, then the Delivered scan, synced | Scene 5, with the API running |
| [`12-courier-401-back-to-login.png`](12-courier-401-back-to-login.png) | The API restarted with a new signing key: the upload gets **401**, the token is cleared, the courier is back at **Login**, and after signing in again the Pending scan is uploaded | Restart the API with a new key (`dotnet user-jwts key --project src/ApexPost.Api --reset --force`), then New Scan and Sync Now |
| [`13-http-redirects-to-https.png`](13-http-redirects-to-https.png) | Plain HTTP is redirected to HTTPS (307 in Development; 308 and HSTS outside it, as `TransportSecurityTests` checks) | `curl.exe -s -i http://localhost:5245/api/v2/shipments/APX-24000011` |

The **network lost** and **network is back** messages (scenes 2 and 3) are not in a picture: turning this PC's network off would also have cut the session that took them. Record them in the video.
