# TDD log: the courier double-scan guard (Week 5, Task 4)

One new business rule, built test-first. The Git history shows the cycle as separate commits labelled `RED:`, `GREEN:` and `REFACTOR:` on branch `week5-s07-tdd-double-scan`, merged into `week5-testing` with a merge commit so the cycle survives into `master`. The strategy behind the choice is in [TESTING-STRATEGY.md](TESTING-STRATEGY.md); the rule's tests are in [ScanRecorderTests.cs](tests/ApexPost.UnitTests/CourierOffline/Recording/ScanRecorderTests.cs).

## 1. The requirement

> **If the same courier records a scan of the same type for the same parcel less than two minutes from an earlier one, the device keeps the first scan, saves nothing new, and tells the courier it was already recorded.**

**Why this rule.** A courier's scanner, or finger, double-taps:
- Each tap creates a scan with a new ID, so the server's scan-ID idempotency (risk R3) cannot see that they are the same.
- For a *Delivered* scan, the server accepts the first scan. It then refuses the second, because the shipment is already Delivered and final.
- The courier is shown a **Failed** scan for a parcel they have just delivered.

The fix belongs on the device, before anything is uploaded.

**Why it is genuinely new.** Two of the brief's example rules already exist in ApexPost:
- "Delivered cannot move back to InTransit" is enforced by `TrackingStatusRules`.
- An upload is capped at 100 scans.

Nothing in ApexPost stopped a double scan.

**What the rule says precisely**, as the tests pin it:
- **Same scan:** the same parcel (tracking number), scan type and courier.
- **Within two minutes:** less than 120 seconds either side of the earlier scan's time. Exactly 120 seconds is a new event. A typed time 30 seconds *before* an identical scan is still the same event.
- **A refused scan does not block:** if the server refused the earlier scan (Failed), the courier may scan again at once.

## 2. RED: the first failing tests

**Commit:** `RED: ScanRecorder keeps the first of two identical scans within two minutes (failing tests)`

The tests came first. To make them fail on an *assertion* rather than a build error, the commit also adds the smallest `ScanRecorder` that compiles. That skeleton is **exactly today's behaviour**, moved out of the console's New Scan screen: build the scan, date it from the clock when no time is given, and save it. The screen itself is not changed yet.

The tests, with Moq for `IScanRepository` and a `FakeTimeProvider` for the clock:

| Test | Kind | On the skeleton |
|---|---|---|
| `Record_FirstScanOfTheParcel_SavesItAsPending` | characterisation: today's behaviour must survive | passes |
| `Record_NoTimeGiven_DatesTheScanWithTheClock` | characterisation | passes |
| `Record_SameScanAgainWithinTwoMinutes_ReturnsTheEarlierScanAndSavesNothing` (0, 1, 119, −30 s) | **the new rule** | **fails, 4 rows** |
| `Record_SameScanTwoMinutesOrMoreLater_SavesASecondScan` (120, 600 s) | the rule's boundary | passes (it always saves) |
| `Record_SoonAfterAScanThatDiffers_SavesASecondScan` (another type, another courier) | what the rule must *not* catch | passes |
| `Record_SoonAfterAScanTheServerRefused_SavesANewScanSoTheCourierCanTryAgain` | what the rule must *not* catch | passes |

The run, `dotnet test tests/ApexPost.UnitTests --filter FullyQualifiedName~ScanRecorderTests`:

```text
ScanRecorderTests.Record_SameScanAgainWithinTwoMinutes_ReturnsTheEarlierScanAndSavesNothing(secondsLater: 1) [FAIL]
ScanRecorderTests.Record_SameScanAgainWithinTwoMinutes_ReturnsTheEarlierScanAndSavesNothing(secondsLater: -30) [FAIL]
ScanRecorderTests.Record_SameScanAgainWithinTwoMinutes_ReturnsTheEarlierScanAndSavesNothing(secondsLater: 0) [FAIL]
ScanRecorderTests.Record_SameScanAgainWithinTwoMinutes_ReturnsTheEarlierScanAndSavesNothing(secondsLater: 119) [FAIL]
  Error Message:
   Assert.True() Failure
Expected: True
Actual:   False
Failed!  - Failed:     4, Passed:     7, Skipped:     0, Total:    11
```

The failure is the right one: the recorder says the double tap is a *new* scan and saves it. That is the defect in the requirement.

## 3. GREEN: the minimal implementation

**Commit:** `GREEN: ScanRecorder returns the earlier scan instead of saving a double scan`

The least code that makes all eleven tests pass: one loop over the parcel's history, added before the save.

```csharp
var scan = ShipmentScan.Record(trackingNumber, scanType, courierId, location, scannedAtUtc ?? clock.GetUtcNow(), notes);
foreach (var earlier in scans.FindByTrackingNumber(scan.TrackingNumber))
{
    if (earlier.ScanType == scan.ScanType && earlier.CourierId == scan.CourierId && earlier.SyncStatus != SyncStatus.Failed
        && (scan.ScannedAtUtc - earlier.ScannedAtUtc).Duration() < TimeSpan.FromMinutes(2))
    {
        return new ScanRecordResult(earlier, AlreadyRecorded: true);
    }
}

scans.Create(scan);
```

Decisions taken by staying minimal:
- **No new query.** `IScanRepository.FindByTrackingNumber` already exists. It is a parameterised SQL++ query over an indexed field, proven on Couchbase Lite in Week 4 by `ScanQueryTests`, and a parcel's history is a handful of scans. A dedicated "latest identical scan" query would add an interface method, a query and an index for no measurable gain.
- **The scan is built first, then compared.** `ShipmentScan.Record` normalises the tracking number and trims the courier ID, so the comparison sees what is stored. An invalid scan still fails exactly as before, before the history is read.
- **`Duration()`** gives "either side" in one expression, which the −30 s row requires.

The run:

```text
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11   (ScanRecorderTests)
Passed!  - Failed:     0, Passed:   592, Skipped:     0, Total:   592   (ApexPost.UnitTests)
Passed!  - Failed:     0, Passed:    90 (Courier.Offline.IntegrationTests), 124 (Courier.IntegrationTests)
```

The design smells this leaves, deliberately, for REFACTOR:
- the two-minute window is a magic number;
- the four-part condition has no name;
- "is this the same event?" is a domain question buried in a loop in the recorder.

## 4. REFACTOR

**Commit:** `REFACTOR: name the repeat rule (ShipmentScan.IsRepeatOf) and its window (ScanRecorderOptions)`

The three smells GREEN left, removed with every test green and **no test file changed** (`git diff -- tests` is empty for this commit):

| Smell | After |
|---|---|
| "Is this the same event?" as a four-part condition buried in the recorder's loop | `ShipmentScan.IsRepeatOf(earlier, window)`: a named domain rule on the scan itself. It is self-contained (it also compares the tracking number, which the loop got implicitly from the query), and the console and MAUI heads share it. |
| A magic `TimeSpan.FromMinutes(2)` | `ScanRecorderOptions.RepeatWindow`, defaulting to `DefaultRepeatWindow` (two minutes), with the reason in its comment. The constructor parameter is optional, so the tests and callers did not change. |
| A `foreach` with an early `return` | one line: `FindByTrackingNumber(…).FirstOrDefault(stored => scan.IsRepeatOf(stored, _repeatWindow))` |

The results:
- **The suites stay green:** `ScanRecorderTests` 11/11, `ApexPost.UnitTests` 592/592, the courier offline and app integration tests 90/90 and 124/124. The domain-independence guard confirms the new domain method uses no Couchbase type.
- **The tests still pin the refactored code.** Changing `IsRepeatOf`'s `< window` to `<= window` for a moment fails exactly `Record_SameScanTwoMinutesOrMoreLater_SavesASecondScan(secondsLater: 120)`.

## 5. Second cycle: the rule reaches the courier app

The first cycle built and tested the rule in the shared courier library, but the console app's New Scan screen still saved scans itself. The second cycle drives the rule into the app from the outside: a scripted courier session (`CourierAppTests`), run through the app's real composition with Couchbase Lite in a temporary folder.

### RED

**Commit:** `RED: a courier who enters the same delivery twice is told it is already recorded (failing app test)`

```text
NewScan_SameDeliveryEnteredTwice_SaysAlreadyRecordedAndKeepsOneScan
  Script: sign in as CR-001; New Scan APX-30000003, Delivered, 14 Elm Street, at 2026-11-02 09:30;
          New Scan apx-30000003 (lower case), Delivered, 14 Elm Street, at the same time; exit.
  Expects: one scan on the device, one "Saved on this device as Pending",
           and "Already recorded on this device: <id of the kept scan>. Nothing new was saved."
```

The same time is typed twice, so the test does not depend on the clock. A background sync may have uploaded the first scan by the time the second is entered, so the test counts the scans on the device, not the Pending ones. A synced scan still counts as a repeat.

The run:

```text
CourierAppTests.NewScan_SameDeliveryEnteredTwice_SaysAlreadyRecordedAndKeepsOneScan [FAIL]
  Error Message:
   Assert.Single() Failure: The collection contained 2 items
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
```

The right failure: the real app stored the double tap.

### GREEN

**Commit:** `GREEN: the New Scan screen records through ScanRecorder and says when a scan is already recorded`

The least change that passes:
- **`NewScanScreen`** takes a `ScanRecorder` instead of an `IScanRepository`, and calls `recorder.Record(…)` where it used to build and save the scan itself.
- **When the result says `AlreadyRecorded`,** it prints `Already recorded on this device: <id>. Nothing new was saved.` and returns, with no "Saved" line and no wait for a change notification, because nothing changed.
- **`AddCourierCore()` registers `ScanRecorder`,** so the console app gets it from the shared library, and so will the future MAUI app.

The run:

```text
NewScan_SameDeliveryEnteredTwice_SaysAlreadyRecordedAndKeepsOneScan   Passed
Passed!  - Failed:     0, Passed:   125, Total:   125   (ApexPost.Courier.IntegrationTests)
Passed!  - Failed:     0, Passed:    90, Total:    90   (ApexPost.Courier.Offline.IntegrationTests)
Passed!  - Failed:     0, Passed:   592, Total:   592   (ApexPost.UnitTests)
```

Left for REFACTOR: the screen still works out "blank means now" with its own clock. The recorder already does that from the same injected clock, so the screen holds a second copy of the rule and a dependency it no longer needs.

### REFACTOR

**Commit:** `REFACTOR: the New Scan screen leaves blank-time-means-now to ScanRecorder and drops its own clock`

With every test green and no test file changed:
- **One copy of the rule.** The screen passes `null` for a blank time, and `ScanRecorder` dates the scan from the injected clock. Before, the screen had its own copy of "blank means now".
- **`NewScanScreen` no longer takes a `TimeProvider`.** It asks and answers; the recorder holds the rules.
- **The result is deconstructed** (`var (scan, alreadyRecorded) = …`). A comment says why no change notification is awaited for a repeat: nothing was written.

The run: `ApexPost.Courier.IntegrationTests` 125/125, including the first scripted session, which leaves the time blank, so "blank means now" is still covered through the real app. `ApexPost.UnitTests` 592/592.

## 6. Production code changed to make it testable

| Change | Why it was needed for the tests | Kind |
|---|---|---|
| **Recording moved out of the console screen** into `ScanRecorder` in `ApexPost.Courier.Offline` | The rule could only have been tested by driving the console. As a class, it is tested in milliseconds with no user interface, and the future MAUI app shares the same code. | extract class |
| **`IScanRepository` passed to `ScanRecorder`'s constructor** | Moq stands in for the device database: `FindByTrackingNumber` returns a chosen history, and `Create` is verified `Times.Never` or `Times.Once` | constructor injection |
| **`TimeProvider` passed to `ScanRecorder` instead of reading the clock** | "Now" for an untimed scan comes from a `FakeTimeProvider`, so `Record_NoTimeGiven_DatesTheScanWithTheClock` is exact and deterministic. The screen's own clock was then removed. | `TimeProvider` instead of `DateTime.Now` |
| **The rule named on the domain object:** `ShipmentScan.IsRepeatOf(earlier, window)` | One small, pure method holds the rule, and the boundary rows pin it (a `<=` mutation fails exactly the 120 s row) | extract method |
| **The window as `ScanRecorderOptions.RepeatWindow`** | The two minutes has a name and a reason, and a test or another app head can set it without editing the rule | options object |
| **`ScanRecorder` registered in `AddCourierCore()`** | The app-level test runs the real composition; the screen gets the recorder exactly as the MAUI head will | dependency injection |

**Not changed:**
- **`IScanRepository`.** The existing, index-backed `FindByTrackingNumber` query was enough, so no interface method, query or index was added (see GREEN).
- **The API.** The rule lives on the device, before any upload.

## 7. The commits

| Commit | Phase |
|---|---|
| `b632b3f` | RED: ScanRecorder keeps the first of two identical scans within two minutes (failing tests) |
| `ea33150` | GREEN: ScanRecorder returns the earlier scan instead of saving a double scan |
| `ff0d5c4` | REFACTOR: name the repeat rule (ShipmentScan.IsRepeatOf) and its window (ScanRecorderOptions) |
| `f87b1bd` | RED: a courier who enters the same delivery twice is told it is already recorded (failing app test) |
| `980e9ea` | GREEN: the New Scan screen records through ScanRecorder and says when a scan is already recorded |
| `4e38d6b` | REFACTOR: the New Scan screen leaves blank-time-means-now to ScanRecorder and drops its own clock |

`git log --oneline` on `week5-testing` shows the six commits in order. The step branch is merged with a merge commit, not squashed, so they stay visible on `master` too.
