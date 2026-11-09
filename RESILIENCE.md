# Courier app resilience

The courier works on a phone network that drops out in lifts, basements and between towns, and the API itself can be restarting or overloaded. This file explains how the courier app ([`ApexPost.Courier`](src/ApexPost.Courier)) calls the API so that a weak network costs time, never scans. The code is [`ApiResilience.cs`](src/ApexPost.Courier/Api/ApiResilience.cs), built on `Microsoft.Extensions.Http.Resilience` (Polly 8).

There are two layers:
- **Within one call**, the resilience pipeline below rides out short failures: a dropped packet, a server restarting, a timeout. It works in seconds.
- **Across calls**, the Part A outbox. Every scan is saved on the device first and stays Pending until the server has it. If a whole sync fails, nothing is lost; the next sync sends the scans again. It works in minutes, whenever the network comes back ([OFFLINE-DESIGN.md](OFFLINE-DESIGN.md)).

## The pipeline

`ApexApiClient` is a typed client from `IHttpClientFactory`. Its handler pipeline is, from the outside in:

```
total timeout (30 s)
  └─ retry (3 retries, exponential backoff with jitter)
       └─ circuit breaker
            └─ attempt timeout (10 s)
                 └─ AuthHeaderHandler → network
```

The order is deliberate:
- **The attempt timeout is innermost,** so each try gets its own 10 seconds, and a hung try becomes a timeout that the retry can act on.
- **The circuit breaker sits inside the retry,** so it counts every attempt, retries included. Four failed tries in a row are four failures, not one.
- **The total timeout is outermost.** It bounds the whole call, waits between retries included, so a sync never hangs for minutes.
- **`AuthHeaderHandler` is inside everything,** so each retry reads the current token again.

## The values, and why

| Setting | Value | Why |
|---|---|---|
| Retries | **3**, so at most 4 tries | Most network blips and server restarts last a few seconds. A fourth failure in a row means the problem is not a blip, and the circuit breaker should decide instead. |
| Backoff | **Exponential from 1 s, with jitter**: about 1, 2 and 4 s | Each wait doubles, so a struggling server gets more room each time. Jitter randomises each wait, so a hundred couriers whose phones reconnect at the same moment do not retry in lockstep and knock the server over again. |
| Attempt timeout | **10 s** | An upload of 20 scans is a few kilobytes and normally takes well under a second. Ten seconds allows for a very slow mobile link, but abandons a connection that has stalled. |
| Total timeout | **30 s** | How long the courier may wait for one sync. Four 10-second attempts plus the waits could take about 47 s, so on the very worst network the last attempt is cut short. That is fine: the scans stay Pending for the next sync. |
| Circuit opens when | **at least 50 % of at least 4 attempts in 30 s fail** | Four failures is exactly one call's tries. So one sync that fails all its retries is enough to decide that the server is down, while one stray failure among successful calls is not. |
| Circuit stays open | **30 s** | Long enough to stop hammering a server that is down and to save the phone's battery and data. Short enough that a courier who comes back into coverage is not kept offline for long. After 30 s one trial call goes through (half-open); its result closes or reopens the circuit. |

All of them can be changed in configuration without a rebuild, under `ApexPostApi:Resilience` (`MaxRetryAttempts`, `RetryBaseDelay`, `AttemptTimeout`, `TotalTimeout`, `BreakerFailureRatio`, `BreakerMinimumThroughput`, `BreakerSamplingDuration`, `BreakDuration`). The tests shorten them to milliseconds, and one test pins the defaults to this table.

## What is retried

A call is retried only if **both** hold:

**1. The failure is transient**, meaning the same request might succeed in a moment:

| Outcome | Retried? | Why |
|---|---|---|
| Network error (`HttpRequestException`: no route, connection reset, DNS) | Yes | The typical mobile blip. |
| Attempt timeout | Yes | The connection stalled; a new one may not. |
| **408** Request Timeout | Yes | The server gave up waiting for the request; sending it again is what 408 asks for. |
| **5xx** (500, 502, 503, 504, …) | Yes | The server, or a proxy in front of it, failed. A restart or a busy moment usually passes. |
| **Any other 4xx**: 400, 401, 403, 404, 409, 429, … | **Never** | The server understood and refused. The same request gets the same answer, and retrying only wastes the courier's data. A 401 needs a new sign-in, not a retry. A 409 on an upload ("another upload was saved at the same moment") and a 429 are left to the outbox's next sync, not hammered within seconds. |

**2. The request is safe to send twice.** GET, HEAD, PUT and DELETE are idempotent by definition: doing them twice has the same effect as once. A POST is retried only when it is marked safe (`ApiResilience.SafeToRetry`), which only the scan upload is. `POST dev/token` is not retried; the courier can simply sign in again.

### Why the scan upload is safe to retry

A retry happens precisely when the app does not know whether the server got the request. The response may have been lost after the server had already stored the scans. Sending them again must therefore not store anything twice. It does not:
- **Each scan carries the ID the device gave it** when it was recorded (`scan::{GUID}`, Part A). The ID never changes, however often the scan is sent.
- **The server stores each scan ID once** (Part B, [API-NOTES.md](API-NOTES.md)). A scan ID it already has counts as `duplicatesSkipped` and changes nothing. A unique key on the scan ID in the database (`AK_TrackingScans_ScanId`) is the last line of defence against two uploads racing.
- **An upload is one database commit.** Either all of its accepted scans are stored or none are, so a retry never meets a half-stored upload.
- **The device marks scans Synced only from the server's answer.** Accepted and duplicate both mean "the server has it". So a retried upload whose first attempt had in fact succeeded ends in exactly the same state as one that succeeded the first time.

## Circuit breaker and working offline

When the circuit **opens**:
- Every API call fails immediately with `BrokenCircuitException`, without touching the network. Nothing waits for a server that is down.
- The app's header shows **`Server unavailable – working offline`**, and the Sync Status screen shows the circuit as open, with the time of the next trial.
- **The courier keeps working:** New Scan saves to the device exactly as before, because saving never needs the server. A sync during the break leaves every scan Pending.

After the break, the circuit is **half-open**. The next call is a trial: if it succeeds, the circuit **closes** and the banner disappears. If it fails, the circuit opens for another 30 s.

## Logging

Every resilience event is logged, under the category `ApexPost.Courier.Api.ApiResilience`:

| Event | Level | Example |
|---|---|---|
| Retry | Warning | `Retry 1 of 3 for POST /api/v2/shipments/APX-24000001/scans in 1043 ms, after 503 ServiceUnavailable` |
| Attempt timeout | Warning | `An attempt was abandoned after 00:00:10` |
| Total timeout | Warning | `The API call was abandoned after 00:00:30 in all` |
| Circuit opened | Warning | `Circuit opened for 30 s after 503 ServiceUnavailable: working offline` |
| Circuit half-open | Information | `Circuit half-open: the next call tries the server again` |
| Circuit closed | Information | `Circuit closed: the server answers again` |

The lines give the method and path, never the request body, the query string or the token; a test checks that the token never appears. Polly's own telemetry, which would repeat each event (and reports timeouts as errors), is turned off in `appsettings.json` (`"Polly": "None"`), and the HTTP client's per-request logging is set to `Warning`.

## Tests

[`ResilienceTests`](tests/ApexPost.Courier.Tests/Api/ResilienceTests.cs) run the app's real composition against a stub network:
- retries after 5xx, 408 and network errors, each try with the same body and token;
- exactly four tries in all when the failure lasts;
- no retry on any other 4xx, or on a POST that is not marked safe;
- a slow attempt abandoned and retried, and the whole call abandoned at the total timeout;
- the circuit opening after one failed sync, the banner on screen, calls failing at once while it is open, and the circuit closing and the banner going when the server answers again;
- the log lines, and that the defaults are the values in this file.

When I made every 4xx retryable and dropped the safe-to-retry check, 7 of these tests failed.
