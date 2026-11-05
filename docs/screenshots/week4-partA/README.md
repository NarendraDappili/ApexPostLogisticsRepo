# Week 4 Part A screenshots

Taken from the console harness on a fresh data folder, with fictional data only:

```powershell
dotnet run --project src/ApexPost.Courier.Offline.Harness -- --data .\courier-demo
```

`courier-demo` is a new, empty folder, so delete it afterwards.

| File | What it shows | Harness steps |
|---|---|---|
| `01-sample-data-listener-log.png` | The listener logging the IDs of the 5 new documents, then the header with `Pending scans (live): 5` and the per-type counts | Answer `y` to the sample data question |
| `02-find-by-tracking-number.png` | Query 1: one parcel's history, oldest first | `2`, then `APX-24000001` |
| `03-list-pending.png` | Query 2: the pending scans | `1` to add a scan for `APX-24000005` (type `2`, location `Central Sorting, Belt 4`), then `3` |
| `04-sync-offline.png` | The outbox with no network: scans stay Pending | `4`, then `n` |
| `05-sync-online-listener-log.png` | The outbox online: a listener line per changed scan, the live count falling to 0, then the result | `4`, then `y` |
| `06-scans-by-type-and-delete.png` | Query 3 (scans per type in the header) and the listener after a delete | `2`, `APX-24000001`, then `5` and `1` |
| `07-second-process-refused.png` | The single-process lock | While the first harness is open, start a second one with the same `--data` folder |

To show a rejected (Failed) scan, add one with a time more than 5 minutes ahead of the current UTC time, then sync.
