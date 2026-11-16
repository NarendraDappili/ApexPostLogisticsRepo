# Week 5 test run

The evidence for Task 7: the whole suite, run from the command line on the developer's machine. Visual Studio's Test Explorer runs the same tests with no set-up ([README](../../README.md#tests)).

| | |
|---|---|
| When | 15 November 2026, the final Week 5 run (step S10b), on `master` as merged (`0357fce`) |
| Machine | Windows 11 Pro, .NET SDK 10.0.402, the local SQL Server default instance, found automatically (`tests/Shared/TestSqlServer.cs`) |
| Build | `dotnet build ApexPost.slnx -c Release -warnaserror`: succeeded, 0 warnings |
| Command | `dotnet test ApexPost.slnx -c Release --no-build --collect "Code Coverage;Format=cobertura" --results-directory TestResults/coverage` |
| Result | **1323 passed, 0 failed, 0 skipped** |

## The summary lines, as printed

```text
Passed!  - Failed:     0, Passed:    90, Skipped:     0, Total:    90, Duration: 6 s - ApexPost.Courier.Offline.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   605, Skipped:     0, Total:   605, Duration: 6 s - ApexPost.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   125, Skipped:     0, Total:   125, Duration: 11 s - ApexPost.Courier.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    23, Skipped:     0, Total:    23, Duration: 14 s - ApexPost.Specs.dll (net10.0)
Passed!  - Failed:     0, Passed:   278, Skipped:     0, Total:   278, Duration: 35 s - ApexPost.Api.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    90, Skipped:     0, Total:    90, Duration: 25 s - ApexPost.Infrastructure.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   112, Skipped:     0, Total:   112, Duration: 1 m 51 s - ApexPost.Web.IntegrationTests.dll (net10.0)
```

## By level of the test pyramid

| Level | Project | Passed | Failed | Skipped |
|---|---|---|---|---|
| Unit | `ApexPost.UnitTests` | 605 | 0 | 0 |
| Integration | `ApexPost.Api.IntegrationTests` | 278 | 0 | 0 |
| Integration | `ApexPost.Courier.IntegrationTests` | 125 | 0 | 0 |
| Integration | `ApexPost.Courier.Offline.IntegrationTests` | 90 | 0 | 0 |
| Integration | `ApexPost.Infrastructure.IntegrationTests` | 90 | 0 | 0 |
| Integration | `ApexPost.Web.IntegrationTests` | 112 | 0 | 0 |
| Acceptance (Reqnroll) | `ApexPost.Specs`: 3 features | 23 scenario cases | 0 | 0 |
| **Total** | | **1323** | **0** | **0** |

Unit 45.7%, integration 52.5%, acceptance 1.7%.

How the total grew during Week 5, from the 1151 tests it started with:

| Step | Added | What |
|---|---|---|
| S4 | 3 | The injected clock pinned |
| S5 | 85 | Boundary tables |
| S6 | 36 | Moq isolation, the HTTP client on a fake handler, the no-network guard |
| S7 | 12 | The double-scan guard, test-first |
| S8 | 12 | Two hand-written Gherkin features |
| S10a | 2 | Gaps the coverage report found |
| S9 | 22 | Copilot-drafted and reviewed: the uploader's unit tests and a third feature |

## Coverage from the same run

Line coverage **96.6%** (5911 of 6118 lines), branch coverage **87.3%** (2222 of 2544) of the production code. Test projects, EF migrations, generated code, Razor views and `Program.cs` are excluded. The per-assembly and per-class table is in [coverage-summary.md](coverage-summary.md), and what it led to is in [docs/testing.md](../testing.md#line-coverage).

CI repeated the run on Linux for every Week 5 pull request and for the merge into `master`, and passed each time. It keeps each run's TRX files (the `test-results` artifact).
