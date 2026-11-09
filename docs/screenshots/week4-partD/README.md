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

## Screenshots (optional, for the report)

| File | What it shows |
|---|---|
| `01-swagger-authorize.png` | Swagger UI's **Authorize** dialog with the **Bearer** and **PartnerApiKey** schemes (v2) |
| `02-courier-offline-banner.png` | The courier app's header with `*** Server unavailable – working offline ***` and Sync Status showing the open circuit |
| `03-courier-auto-sync.png` | The log lines `Network is back …` and `Auto-sync (network back): 2 synced …` |
| `04-http-401-403.png` | A 401 and a 403 ProblemDetails in the `.http` response pane |
