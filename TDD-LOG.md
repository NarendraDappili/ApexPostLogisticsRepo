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

*Written when the REFACTOR commit is made.*

## 5. Production code changed to make it testable

*Completed at the end of the cycle.*
