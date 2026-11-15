# Week 5 test run

The evidence for Task 7: the whole suite, run from the command line on the developer's machine. Visual Studio's Test Explorer runs the same tests with no set-up ([README](../../README.md#tests)).

| | |
|---|---|
| When | 15 November 2026, step S10a (before the Copilot step; refreshed in S10b) |
| Machine | Windows 11 Pro, .NET SDK 10.0.402, the local SQL Server default instance, found automatically (`tests/Shared/TestSqlServer.cs`) |
| Build | `dotnet build ApexPost.slnx -c Release -warnaserror`: succeeded, 0 warnings |
| Command | `dotnet test ApexPost.slnx -c Release --no-build --collect "Code Coverage;Format=cobertura" --results-directory TestResults/coverage` |
| Result | **1301 passed, 0 failed, 0 skipped** |

## The summary lines, as printed

```text
Passed!  - Failed:     0, Passed:    90, Skipped:     0, Total:    90, Duration: 6 s - ApexPost.Courier.Offline.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   594, Skipped:     0, Total:   594, Duration: 5 s - ApexPost.UnitTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   125, Skipped:     0, Total:   125, Duration: 10 s - ApexPost.Courier.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 12 s - ApexPost.Specs.dll (net10.0)
Passed!  - Failed:     0, Passed:   278, Skipped:     0, Total:   278, Duration: 31 s - ApexPost.Api.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    90, Skipped:     0, Total:    90, Duration: 21 s - ApexPost.Infrastructure.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   112, Skipped:     0, Total:   112, Duration: 1 m 35 s - ApexPost.Web.IntegrationTests.dll (net10.0)
```

## By level of the test pyramid

| Level | Project | Passed | Failed | Skipped |
|---|---|---|---|---|
| Unit | `ApexPost.UnitTests` | 594 | 0 | 0 |
| Integration | `ApexPost.Api.IntegrationTests` | 278 | 0 | 0 |
| Integration | `ApexPost.Courier.IntegrationTests` | 125 | 0 | 0 |
| Integration | `ApexPost.Courier.Offline.IntegrationTests` | 90 | 0 | 0 |
| Integration | `ApexPost.Infrastructure.IntegrationTests` | 90 | 0 | 0 |
| Integration | `ApexPost.Web.IntegrationTests` | 112 | 0 | 0 |
| Acceptance (Reqnroll) | `ApexPost.Specs` | 12 scenarios | 0 | 0 |
| **Total** | | **1301** | **0** | **0** |

Unit 45.7%, integration 53.4%, acceptance 0.9%.

## Coverage from the same run

Line coverage **96.6%** (5910 of 6118 lines), branch coverage **87%** (2160 of 2482) of the production code. Test projects, EF migrations, generated code, Razor views and `Program.cs` are excluded. The per-assembly and per-class table is in [coverage-summary.md](coverage-summary.md), and what it led to is in [docs/testing.md](../testing.md#line-coverage).

CI repeats the run on Linux for every pull request and keeps each run's TRX files (the `test-results` artifact).
