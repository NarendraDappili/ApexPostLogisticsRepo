# ADR 0029: The courier app is a console program, with stand-ins for the phone's services

Status: Accepted (Week 4, Part D).

## Context

The brief asks for `ApexPost.Courier` as a .NET MAUI app for Android and Windows. It would call the v2 API through `IHttpClientFactory` and sync the Part A offline scans. A first plan kept the app's logic in a `net10.0` library and added a thin MAUI shell, built by a second, Windows-only CI job. Before any MAUI code was written, the course owner decided that the client should be a console app for now. The CI runs on Ubuntu, which cannot build MAUI, and a console app can be built, tested and demonstrated anywhere the rest of the solution can.

## Decision

- **`ApexPost.Courier` is a console program** on the Generic Host. Its parts are the ones the MAUI app would have:
  - a typed `ApexApiClient` from `AddHttpClient<ApexApiClient>()`;
  - `AuthHeaderHandler`;
  - the resilience pipeline ([ADR 0031](0031-courier-sync-through-one-resilient-client.md));
  - source-generated JSON;
  - the four screens: Login, New Scan, Pending Scans and Sync Status.

  The screens read and write through a `TextReader` and `TextWriter`, so tests drive whole sessions.
- **It reuses the Part A store** (`ApexPost.Courier.Offline`) unchanged. Its own database folder is not the harness's, because an embedded database belongs to one process.
- **Phone-only services get console stand-ins** behind small interfaces, so a MAUI head could swap them back:

  | MAUI | Console stand-in | Difference |
  |---|---|---|
  | `SecureStorage` | `InMemoryTokenStore` (`ITokenStore`) | The token lives in memory only, so it does not survive a restart; the courier signs in again. |
  | `Connectivity.ConnectivityChanged` | `SystemNetworkStatus` (`INetworkStatus`), from `NetworkChange` | "Online" means an adapter that is up and has a default gateway, so virtual adapters do not count. |
  | Per-platform base URLs (emulator, Windows) | `ApexPostApi:BaseAddress` in `appsettings.json`, launch profiles, the command line | Same idea: configuration, not code. |
  | Pages and navigation | Console screens; a 401 returns to Login at the next screen | No UI thread to marshal to; log lines go between the menu lines. |
- **Everything builds and is tested on the existing Ubuntu CI;** there is no Windows job.

## Consequences

- **The whole Part D client is in CI:** typed client, resilience, JSON, sync and token handling (116 tests).
- **The demo video shows a console window, not a phone.** Airplane mode works on a laptop, because the app watches the operating system's network. The circuit breaker is shown by stopping the API.
- **A MAUI head is still a small addition.** It would register `SecureStorage` and `Connectivity` behind the same two interfaces, and put pages over the same services. The app's logic would then move into a class library that both heads reference.
- **The token store is weaker than a phone's only in one way:** it does not persist. That is the safe side; a token on disk would need the OS's protection (DPAPI, Keychain), which MAUI's `SecureStorage` provides.
