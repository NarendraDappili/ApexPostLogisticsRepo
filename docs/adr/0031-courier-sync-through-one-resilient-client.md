# ADR 0031: The courier app syncs through one resilient typed client, and a lost answer is always safe to send again

Status: Accepted (Week 4, Part D).

## Context

The courier's connection drops without warning, and the API can be restarting. The brief asks for retries with backoff and jitter on transient failures only, a circuit breaker with an offline banner, per-attempt and total timeouts, friendly errors, automatic sync when connectivity returns, and a token that leads back to Login on a 401. Part A's outbox already saves every scan before sending it and marks scans only from the server's answer.

## Decision

- **One typed client, one pipeline.** Every call goes through `ApexApiClient`, whose handlers are, from the outside in:
  - a total timeout (30 s);
  - a retry (3 retries, exponential from 1 s with jitter);
  - a circuit breaker (opens when half of at least 4 attempts in 30 s fail; stays open 30 s);
  - an attempt timeout (10 s);
  - `AuthHeaderHandler`.

  The values and the reasons for them are in [RESILIENCE.md](../../RESILIENCE.md).
- **Retry only what is transient and safe:**
  - **transient:** network errors, timeouts, 408 and 5xx, never another 4xx;
  - **safe:** idempotent methods, plus the scan upload, which is marked safe because the server stores each device-made scan ID once. `POST dev/token` is never retried.
- **Two outcomes for every call, both with a message for the courier.**
  - `ApexApiException`: the server answered with an error. Its ProblemDetails are parsed; a 5xx shows only the reference number.
  - `ApiUnavailableException`: there was no answer, because of no network, a timeout or an open circuit.

  The outbox shows those messages as they are.
- **The real uploader decides per shipment, from the answer:**
  - accepted or already stored → Synced;
  - rejected, unknown shipment (404) or named in a 400 → Failed with the reason, so a bad scan cannot block the outbox for ever;
  - 403 or 409 → left Pending for a later sync;
  - no answer, 401 or 5xx → the batch stops, and everything stays Pending.
- **Automatic sync** runs when the network comes back, at start-up, and every minute while online. The last is how an open circuit finds a recovered API without the courier pressing anything.
- **The token lives in memory only.** A 401 for the token that was sent, or its expiry, clears it and sends the courier back to Login at the next screen.

## Consequences

- **A weak network costs time, never scans.** Every path that is not a clear answer leaves the scan Pending, and sending it again is safe by construction. The real end-to-end runs proved the API stopping and restarting, its signing key changing, and an unknown shipment.
- **Retries and circuits are visible.** Every retry, timeout and circuit change is logged, the header shows the banner, and Sync Status shows the circuit. Polly's own duplicate telemetry is turned off.
- **The timing behaviour is tested with shortened values** (milliseconds instead of seconds) on the app's real composition, with a stub network and an in-memory fake of the API's rules. One test pins the production defaults to RESILIENCE.md.
- **A circuit breaker per app, not per endpoint.** The app talks to one API, so one open circuit means "the server is down" for every screen. An app with several back ends would need one pipeline per authority.
