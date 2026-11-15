# ADR 0032: The courier app's logic lives in a class library that every app head references

Status: Accepted (Week 5, step S2). Carries out the consequence foreseen in [ADR 0029](0029-courier-app-as-console-program.md).

## Context

In Week 4 the courier app was built as a console program (ADR 0029), and all its logic sat inside that executable:
- the typed `ApexApiClient`, `AuthHeaderHandler` and the resilience pipeline;
- the uploader and automatic sync;
- the sign-in session.

ADR 0029 noted that a MAUI head would need this logic in a class library that both heads reference.

Week 5 makes that move worth doing now, for three reasons:
- **The tests should target code that survives into MAUI.** The Week 5 client, sync and BDD tests ([TESTING-STRATEGY.md](../../TESTING-STRATEGY.md)) would otherwise be written against an executable and re-homed when the MAUI app arrives.
- **An executable is awkward to test from several projects.** Its types are `internal`, and granting `InternalsVisibleTo` to a project that also references `ApexPost.Api` makes two `Program` classes visible at once.
- **A MAUI app cannot cleanly reference a console executable**, and should not inherit its screens.

## Decision

- **New class library `ApexPost.Courier.Core`** (`net10.0`), referencing `ApexPost.Courier.Offline`. It contains:
  - `Api/`: `ApexApiClient`, `AuthHeaderHandler`, the resilience pipeline and its options, the v2 records, the errors, source-generated JSON, `ServerAvailability`, `PastedToken`, and the **`ITokenStore`** seam.
  - `Sync/`: `ApiShipmentUploader`, `AutoSyncService`, `SyncService`, `SyncHistory`, and the **`INetworkStatus`** seam.
  - `CourierSession`.
  - **`AddCourierCore(configuration)`**: one registration of all of the above.
- **Each app head supplies only what depends on the device:**
  - its screens;
  - a `TimeProvider`;
  - where the offline store lives;
  - an `ITokenStore`;
  - an `INetworkStatus`.

  The console app keeps `InMemoryTokenStore` and `SystemNetworkStatus`. A MAUI app would add a `SecureStorage` store and a `Connectivity` status behind the same interfaces.
- **The library's types are public.** It is consumed by more than one head, and the seams must be implementable outside it. Four members stay `internal`, visible to the courier tests only:
  - the retry predicates;
  - the circuit callbacks;
  - `AutoSyncService.RunOnceAsync`.
- **Namespaces are unchanged** (`ApexPost.Courier.Api`, `ApexPost.Courier.Sync`, `ApexPost.Courier`), so the move is a pure move. `git mv` keeps each file's history.

## Consequences

- **No behaviour changed.** All 1151 tests pass unchanged, and the real console app starts, signs in offline and closes its database as before.
- **One guard was widened.** The test that forbids `new HttpClient()` scanned only `src/ApexPost.Courier`, so it would have silently stopped covering the moved client code. It now scans both projects. It was checked by planting a `new HttpClient(` in the library, watching the test fail, and removing it.
- **Debugging without the API is quiet.** `AuthHeaderHandler` is marked `[DebuggerNonUserCode]`. When the API is down, a normal case for an offline-first app, the transport's `HttpRequestException` passes through the handler into the resilience handler, which catches it and retries. Visual Studio's "Just My Code" used to stop there on every attempt with "user-unhandled", although the app handles the failure and signs in offline. The compiler copies the attribute onto the async state machine, so the debugger treats that frame as framework code. Nothing changes at run time.
- **Week 5 test projects** (`ApexPost.UnitTests`, `ApexPost.Specs`) reference the library directly, with no executable and no `Program` clash.
- **A MAUI head becomes:** pages, two adapters and a call to `AddCourierCore`. The logic it relies on is already tested.
- **More public surface.** Changing a public type now affects every head. The v2 records remain guarded by the contract compatibility test.
