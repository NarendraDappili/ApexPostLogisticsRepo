# ADR 0018: Map application exceptions to 409, 404 and 400 Problem Details

Status: Accepted.

## Context

Week 2 returned a generic 500 Problem Details for every unhandled exception. Week 3 requires:

| Error | Status |
|---|---|
| `DbUpdateConcurrencyException` | 409 |
| Unique-key violations | 409 |
| Not found | 404 |
| Domain errors | 400 |

The difficulty is the last row. The domain signals broken rules with `ArgumentException` and `InvalidOperationException`, but the framework and libraries throw the same types for real bugs. Mapping every `InvalidOperationException` to 400 would turn server faults into "your request was wrong" and could leak internal messages.

## Decision

### Exception types

- Application defines the exceptions that carry a client meaning:
  - `NotFoundException`;
  - `ConflictException`, with subclasses `ConcurrencyConflictException` and `DuplicateKeyException`;
  - `RuleViolationException`, which the stored procedures' `THROW 500xx` errors become.
- `EfUnitOfWork` translates the database errors:
  - `DbUpdateConcurrencyException` becomes `ConcurrencyConflictException`;
  - SQL errors 2601/2627 become `DuplicateKeyException`.
- Workflows throw `NotFoundException` subclasses for missing records, for example `CounterTransactionNotFoundException`.

### One mapping, shared

`ExceptionProblemMapping.Map` is used by both `ProblemDetailsExceptionMiddleware` and the audit filter, so the response and the audit record always agree:

| Exception | Status | Detail returned |
|---|---|---|
| `ConflictException` (and subclasses) | 409 | The application message |
| `NotFoundException` | 404 | The application message |
| `RuleViolationException` | 400 | The procedure's message |
| `ArgumentException` / `InvalidOperationException` **thrown by `ApexPost.Domain` or `ApexPost.Application` code, directly or through a guard helper** (`ArgumentException.ThrowIfNullOrWhiteSpace`, `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIf…`) | 400 | The message, without the "(Parameter '…')" suffix |
| Anything else, including the same types thrown by framework or library code | 500 | None: only `traceId` and `correlationId` |

**How "thrown by business code" is decided.**
- **The rule:** the first stack frame is examined. Frames of the three guard-helper types (`ArgumentException`, `ArgumentNullException`, `ArgumentOutOfRangeException`) are skipped. The next frame must belong to Domain or Application.
- **Why not skip every .NET frame:** an earlier version skipped every frame from the .NET core library. A bug in business code that fails inside a .NET method was then reported as the user's mistake (400) with the framework's text, for example `List<T>` index out of range, a duplicate `Dictionary` key, "Collection was modified" or `Nullable.Value` without a value. With the current rule, the throwing frame is `List`, `ThrowHelper` or `Nullable`, so these stay 500 without details. `ProblemDetailsMappingTests` checks both directions.
- **Why not dedicated domain exception types,** which would need no stack trace at all:
  - The domain signals rules with `ArgumentException` and `InvalidOperationException`.
  - The Week 2 business-rule tests assert those exact types (`Assert.Throws<ArgumentException>` and others), and the brief requires them to pass unchanged.
  - A subclass would fail those exact-type assertions.
  - New Week 3 rules use `RuleViolationException`, `NotFoundException` and `ConflictException` instead.

### MVC forms

MVC forms show a business error on the form, and a conflict also sets status 409 (`ControllerErrorExtensions.UseConflictStatus`), so the status is correct for both browsers and tests.

- **Forms use the same rule as the middleware.** A controller catches only what `ExceptionProblemMapping.Map` recognises, with `catch (Exception e) when (ExceptionProblemMapping.Map(e) is { } problem)`, and shows `problem.Detail`. It never catches every `ArgumentException` or `InvalidOperationException`. An EF Core or framework `InvalidOperationException` therefore reaches the middleware and becomes a 500 without its message, instead of being displayed. A web test proves this with a library exception thrown during checkout.
- **Void and refund redisplay the Manage page.** A refused reversal (missing sale, wrong state, conflict, missing reason) shows the reason on the Manage page with its status code (404, 400 or 409), instead of returning raw Problem Details JSON to the manager's browser. The audit trail still records that status.

## Consequences

- **Correct statuses for API clients and tests.** A stale sale gets 409, a missing sale 404, and a broken rule 400 with a message written for the user.
- **No leaks.** Framework faults stay 500 and never reveal their message.
- **One cost:** classifying by the stack frame's assembly depends on stack traces, which is slightly more expensive. It only runs for exceptions that already escaped the controller.
- **Tests:** `ProblemDetailsMappingTests` cover each row of the table, including "the same exception types from framework code stay 500 without details". The Web tests cover 409, 404 and 400 through real requests.
