# Offline design: the courier device store

Couriers record pickups and deliveries where there is often no signal. The courier device keeps its scans in an embedded Couchbase Lite database, `apexpost_courier`, and sends them to ApexPost later. The code is in [`src/ApexPost.Courier.Offline`](src/ApexPost.Courier.Offline); the console harness is [`src/ApexPost.Courier.Offline.Harness`](src/ApexPost.Courier.Offline.Harness).

## 1. Why the app saves locally first

A scan is a fact about the physical world: the parcel changed hands at that place and time. If saving it depended on the network, a courier in a basement or on a rural road would have three bad choices: wait, write it on paper, or skip it. So every scan follows the same path, whether or not there is a network:

1. **Save on the device.** `ShipmentScan.Record` creates the scan with a new `scan::{Guid}` ID and `syncStatus = Pending`, and the repository writes it to local disk. This takes a few milliseconds and cannot fail for network reasons.
2. **Upload later.** `OutboxService` reads the Pending scans, uploads them in batches of 20, and marks each one `Synced` or `Failed` from the server's answer (the outbox pattern).
3. **If the upload fails** (no network, server down), nothing changes on the device. The scans stay Pending and the next run sends them again.

This has three consequences:
- **One code path.** The app never asks "am I online?" before saving, so offline is not a special case with its own bugs.
- **Nothing is lost.** The device is the first durable copy, and a crash between saving and uploading loses nothing.
- **Retrying is safe.** The ID is created on the device and travels with the scan, so the server can recognise a scan it already has and skip it (Part B). An upload that timed out after the server stored it simply comes back as a duplicate on the retry and is marked Synced.

## 2. How conflicts would be handled

A conflict means two copies of the same record changed independently, and someone must decide which version wins.

| | Last-write-wins (LWW) | The server decides |
|---|---|---|
| **How it works** | The version with the latest timestamp (or the last one saved) replaces the other. | The device sends what it saw; the server applies its own rules and answers accepted, duplicate or rejected. |
| **Good for** | Data that belongs to one user and has no business rules, such as a preference. | Shared data with rules, such as a shipment's status, which may only move Created → PickedUp → InTransit → OutForDelivery → Delivered / Failed. |
| **Risk** | It silently discards the other change, and device clocks can be wrong, so the "latest" change may not be the real latest. | The device must be able to show and handle a rejection. |

**What ApexPost does:**
- **Scans have almost no conflicts by design.** A scan is an append-only event with a device-generated ID; nobody else edits it. Two couriers scanning the same parcel create two scans, not one conflicting record.
- **The shipment status is decided by the server.** The server rejects a scan that breaks the status rules, or whose time is in the future (the fake uploader already does this). The scan becomes `Failed` on the device with the reason, so the courier sees it instead of the server silently overwriting it.
- **On the device, the courier's latest edit wins.** It is a single user on a single device, so local last-write-wins is safe there. The outbox reloads each scan before marking it, so a correction made while an upload was in flight is not overwritten. That scan stays Pending and is sent again.
- **Known limit:** once a scan is `Synced`, the domain refuses corrections, because the server would skip the same ID as a duplicate. A courier who made a mistake records a new scan instead, which also keeps the history honest.

## 3. Why an embedded database must not be shared by several processes

Couchbase Lite is a library inside the app's process, not a server. Each open database keeps state in that process's memory: caches, query state, the sequence it has notified listeners up to, and the in-progress transaction. Two processes opening the same files would each have their own copy of that state:
- One process would never raise change notifications for the other's writes. The live "pending scans" count would be wrong.
- Their caches and indexes would disagree about what is on disk.
- Concurrent writes would be serialised only by file locks, if at all. That risks "database is busy" failures or, in the worst case, corruption.

Couchbase Lite does not support opening one database from several processes. Several apps or services that need the same data should each have their own database and synchronise through a server (Sync Gateway), not by sharing files.

**How the code enforces it:** `CourierDatabase` takes an exclusive lock on `apexpost_courier.lock` in the database folder before opening. A second harness started on the same folder stops with *"already open in another process"*. The operating system releases the lock when the process exits, even after a crash, so a stale lock file never blocks a restart. Inside one process, the app shares one `CourierDatabase` instance; Couchbase Lite's objects are thread-safe for that.
