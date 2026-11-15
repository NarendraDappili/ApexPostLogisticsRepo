# Week 5 viva: the ApexPost testing strategy

A five-minute talk on the strategy and how it was carried out, with the evidence for each claim. The brief asks the viva to cover:
- the test pyramid;
- the framework choice;
- one mock and what it does not prove;
- one Copilot test that was rejected.

The full reasoning is in [TESTING-STRATEGY.md](../../TESTING-STRATEGY.md).

## The roadmap

![Week 5 roadmap of ApexPost: S1 strategy first leads to S2 MAUI-ready courier core and S3 test projects; S3 leads to S4 determinism and S5 unit testing fundamentals; S4 leads to S6 mocking and isolation; S5 leads to S7 TDD and S8 BDD; S6 also leads to S7; S8 leads to S9 Copilot; S6, S7 and S9 lead to S10 results, reflection and viva. Done steps have a green border.](week5-roadmap.svg)

The order is driven by dependencies, not by the order of the brief's tasks:
- **The strategy comes first**, because the brief requires it before any new test.
- **The courier core and the project layout come next**, because every later test is written into them.
- **Determinism comes before mocking**, because the Moq tests need `FakeTimeProvider`.
- **Unit testing fundamentals come before TDD and BDD**, because both build on its conventions.
- **Copilot follows BDD**, because one of its drafts is a feature file.
- **Everything ends in the results and the viva.**

| Step | Brief task | What it means in ApexPost | Evidence | Status |
|---|---|---|---|---|
| S1 Strategy first | Task 1 | Risk map, pyramid, xUnit choice, review of all 102 existing test classes, data access strategy, what is not tested | [TESTING-STRATEGY.md](../../TESTING-STRATEGY.md) | ✓ done |
| S2 MAUI-ready courier core | enabling | The courier client and sync code moves from the console app into `ApexPost.Courier.Core`, so the tests target code the MAUI app will reuse | [ADR 0032](../adr/0032-courier-core-library-for-every-app-head.md), `src/ApexPost.Courier.Core` | ✓ done |
| S3 Test projects | Task 2 | `ApexPost.UnitTests`, the `*.IntegrationTests` projects (one per runtime dependency), `ApexPost.Specs`; the test database is found with no manual set-up | `tests/`, [docs/testing.md](../testing.md#test-projects) | ✓ done |
| S4 Determinism | constraints | `TimeProvider` everywhere; fixed instants in tests; no system clock except the documented JWT exception | [TESTING-STRATEGY.md §8](../../TESTING-STRATEGY.md#8-determinism), review table | ✓ done |
| S5 Unit testing fundamentals | Task 2 | AAA; `Method_Scenario_ExpectedResult`; boundary theories (status matrix, weight, tracking number, scan-time skew); exception tests | [TESTING-STRATEGY.md §4.3](../../TESTING-STRATEGY.md#43-conventions-in-every-test-project), `tests/ApexPost.UnitTests` | ✓ done |
| S6 Mocking & isolation | Task 3 | Moq on the tracking service, outbox and Week 3 shipping service; the client on a fake `HttpMessageHandler`; SQL Server instead of a mocked `DbContext`; `WebApplicationFactory` with real JWT | `TESTING-STRATEGY.md` §7, §9 | planned |
| S7 TDD: double-scan guard | Task 4 | A second identical scan within 2 minutes is not saved again, built RED → GREEN → REFACTOR | `TDD-LOG.md`, Git history | planned |
| S8 BDD with Reqnroll | Task 5 | `ShipmentCreation.feature` (API level) and `ScanSync.feature` (courier device to API) | `tests/ApexPost.Specs` | planned |
| S9 Copilot test drafting | Task 6 | Copilot drafts for `OutboxService` and one feature; review decisions; two break-it proofs | `COPILOT-LOG.md` | planned |
| S10 Results, reflection & viva | Task 7 | The full run per level, coverage as evidence, the reflection, this presentation | README, `docs/testing.md` | planned |

---

## Talk track (5 minutes)

### 0:00–0:30 The problem

- ApexPost now has four moving parts: a SQL Server data layer, an offline Couchbase Lite courier store, a versioned API, and a resilient client.
- The question is not "how many tests". It is: **is it safe to change, and can I explain why?**
- Week 5 started from 1151 passing tests. The work was to **review them against a strategy** and fill the gaps. Writing more tests for the sake of numbers was never the goal.

### 0:30–1:30 Risk map and pyramid

- **Show:** the risk map in `TESTING-STRATEGY.md` §2.
- Name the top four risks:
  - status transitions;
  - duplicate tracking numbers;
  - scan de-duplication;
  - the offline outbox ("a courier's proof of delivery must never be lost").
- **The rule:** each risk is tested at the cheapest level that can *prove* it.
  - Status rules are unit tests.
  - A duplicate-key race can only be proven on SQL Server.
- **The pyramid** (§3.3): unit about 41% → ~45–50%, integration ~45–50%, BDD a few percent.
- **Why the middle is wide:** ApexPost's costliest risks sit at boundaries: a unique index, SQL++ queries, the authentication pipeline, the wire contract. Mocking them would swap proof for assumption.

### 1:30–2:15 Framework choice

- **Answer:** xUnit, one framework across the whole solution.
- **Reasons, in order of weight:**
  1. Consistency: every existing test is xUnit.
  2. Isolation: a new class instance per test, so state cannot leak.
  3. Fixtures: they already manage the throwaway SQL databases.
  4. Theories: `[Theory]`/`[InlineData]` boundary tables.
  5. Reqnroll supports xUnit.
- **What NUnit would have given:** combinatorial attributes. We don't need them, because our boundaries are explicit tables.

### 2:15–3:15 One mock and what it does not prove

- **Show:** the `ShipmentTrackingService` Moq test for a duplicate tracking number (*filled in after S6*).
- **What it proves:** when the repository says the number exists, the service refuses and never calls `Add` or `SaveChangesAsync` (`Times.Never`).
- **What it does not prove:** that the unique index exists, or that two simultaneous creates become SQL error 2601/2627 and then a 409.
- **Where that gap is covered:** `TrackingPersistenceTests` and `ShipmentEndpointTests` on a real SQL Server.
- **Why `DbContext` is never mocked:** LINQ to Objects is not SQL. No constraints, no translation, no interceptors (§9.3).

### 3:15–4:00 TDD and BDD

- **TDD:** the double-scan guard. A double-tapped *Delivered* scan used to come back as Failed, because the second scan reached a finished shipment.
  - **Show:** the RED, GREEN and REFACTOR commits and `TDD-LOG.md` (*filled in after S7*).
  - The change made for testability: the rule moved out of the console screen into `ScanRecorder`, with an injected `TimeProvider`.
- **BDD:** two features in business language.
  - `ShipmentCreation` runs at API level, because only there do validation, the unique index and the refusal wording all apply.
  - `ScanSync` runs from the courier device to the API, because the promise spans both sides.
  - **Show:** the Background, the Scenario Outline, context injection and hooks (*filled in after S8*).

### 4:00–4:40 The Copilot test that was rejected

- **Show:** the `COPILOT-LOG.md` entry (*filled in after S9*): the prompt, the draft, why it was rejected (for example, an assertion that recomputed the expected value with the same logic as the code), and what replaced it.
- **The proof that kept tests matter:** the production code was broken on purpose, the test failed, and the code was restored. Both runs are in the log.

### 4:40–5:00 Results and reflection

- The final run, per level (passed / failed / skipped), and the coverage comment (*filled in after S10*).
- **One sentence on most confidence per minute:** *(written at the end of the week).*

---

## How it was achieved: the step log

Each step is a branch merged into `week5-testing` with a merge commit, so the history (including the TDD cycle) reaches `master` intact.

| Step | Task | What was done | Branch / commit | Tests after |
|---|---|---|---|---|
| S1 | 1 | `TESTING-STRATEGY.md` written **before any new test**: the risk map, pyramid, framework choice, a review of all 102 existing test classes, the data access strategy, and what is not tested. Also this presentation and the roadmap diagram. | `week5-s01-testing-strategy` | 1151 |
| S2 | enabling | The courier client, resilience, uploader, automatic sync and session moved from the console executable into the class library `ApexPost.Courier.Core` (ADR 0032). The console app keeps its screens and two device adapters, and calls `AddCourierCore()`. A pure move: all 1151 tests unchanged and green. One guard was widened: the "never `new HttpClient()`" scan would otherwise have stopped covering the moved client; this was proven by planting a probe and watching the test fail. | `week5-s02-courier-core` | 1151 |
| S3 | 2 | The tests were reorganised by level. Domain and Application tests, plus the pure classes from the other projects, became `ApexPost.UnitTests` (457). The four other test projects were renamed `*.IntegrationTests`, one per real dependency (694). The solution groups the projects into **1 Unit** and **2 Integration**. The test-case list is identical before and after (by class, method and arguments). The SQL Server tests now find a server by themselves: `APEXPOST_TEST_SQLSERVER`, then the local instance, then LocalDB, else a clear failure. The LocalDB fallback was checked with the instance unreachable. CI runs unit tests before SQL Server starts, then each integration project, and keeps TRX results. | `week5-s03-test-projects` | 1151 |
| S4 | constraints | Determinism. Production code no longer reads `DateTimeOffset.UtcNow`: sales and stock adjustments require their time, events must be given theirs (`required`), and four services and the audit filter take the injected `TimeProvider`. No test reads the clock: `FakeTimeProvider` (replacing three home-made clocks) where the code takes a clock; fixed far instants (scans before any run or in 2100, tokens valid 2000–2100) where a host keeps the real one; the SQL and Web report tests anchored on one fixed or stored day, so midnight UTC cannot break them; `Stopwatch` waits. Three new tests pin the injected time, and one was proven to fail when the system clock is put back. Suite run twice in a row, green. | `week5-s04-determinism` | 1154 |
| S5 | 2 | Unit testing fundamentals. The conventions are written down (TESTING-STRATEGY.md §4.3): `Method_Scenario_ExpectedResult` for new tests, one Act, `// Arrange // Act // Assert`, named theory rows, exceptions checked by parameter, value and message. 85 new boundary cases fill the gaps the review found: the full status table (36 pairs) and the scan-decision table (24 pairs), each guarding its own completeness; the Week 3 30 kg ceiling, which had no test; the scan-time limit to the millisecond, with `FakeTimeProvider`. The tracking-number format and the tracked weight were already covered, so they were not duplicated. Three deliberate rule breaks were each caught. | `week5-s05-unit-fundamentals` | 1239 |

---

## Questions to expect

| Question | Short answer |
|---|---|
| Why not one `ApexPost.IntegrationTests` project? | Web and API both declare a global `Program`, so one assembly cannot host both without extern aliases. Each integration project boots one real dependency. The brief allows a documented equivalent (`TESTING-STRATEGY.md` §4.2). |
| Why not SQLite in-memory? | It lacks our 11 stored procedures, rowversion concurrency, the 2601/2627 duplicate-key errors and `DateTimeOffset` ordering. It would prove a database we don't ship. |
| Why keep hand-written fakes next to Moq? | A stateful fake (the in-memory API, the tracking store) reads better than a chain of `Setup` calls. Moq is for interactions: "called once", "never called". |
| Do any tests still read the clock? | No. Production code takes `TimeProvider`, and the tests use `FakeTimeProvider` or a fixed instant. Where a host must keep the real clock (the courier retry pipeline, the Web cookies, JWT lifetime checks), the tests use instants far from any real "now": tokens valid 2000–2100, scans in 2100. The Web report is anchored on the sale's stored day, so midnight cannot break it. |
| What about the MAUI app? | Its client and sync code is in `ApexPost.Courier.Core` and already tested. MAUI only adds pages (out of scope) and two adapters behind seams that have contract tests. |
