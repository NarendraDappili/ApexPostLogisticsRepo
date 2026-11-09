# ADR 0026: The tracking API maps errors by exception type only

Status: Accepted (Week 4, Part B).

## Context

The tracking API must return ProblemDetails for every error, through an `IExceptionHandler`, and must never expose a stack trace outside Development.

Week 3's web application maps exceptions with `ExceptionProblemMapping` ([ADR 0018](0018-exceptions-to-problem-details-status-codes.md)). The domain signals broken rules with `ArgumentException` and `InvalidOperationException`, the same types .NET throws for bugs. So that mapping reads the stack trace to see whether business code threw. That works, but it is subtle:
- it had to be fixed for JIT inlining;
- CI runs the error tests a second time with full optimisation to keep it honest.

## Decision

The API does not inspect stack traces. The error's type decides alone.

- **`ShipmentTrackingService` builds every domain value inside one helper.** An `ArgumentException` there can only be a bad input value, so the helper rethrows it as the existing `RequestValidationException`. The message names the request field, for example `weightKg: The weight must be between 0.1 and 70 kg.`
- **The service's other errors are typed.** Not-found errors derive from `NotFoundException`, and state conflicts from `ConflictException`: a duplicate tracking number, a status move that is not allowed, deleting after Created, a concurrent upload.
- **`ApiExceptionHandler` maps by type:**
  - `RequestValidationException` → 400 `ValidationProblemDetails`;
  - `NotFoundException` → 404;
  - `ConflictException` → 409.

  These messages are written for the caller and are returned in every environment.
- **Any other exception is a 500.** That includes an `ArgumentException` from a library. It is logged with its stack trace. The response gives a fixed message and the `traceId`, and adds the exception's type, message and stack trace only in Development.
- **Bodiless framework errors** (404, 405, 415) get ProblemDetails from `UseStatusCodePages`.
- **Request DTO validation** runs before the service, through `[ApiController]`. Its keys and messages use the JSON names, the same names the service uses. The serializer's own error text is not returned, because it names internal types.

## Consequences

- **A bug is never reported as "your request was wrong".** The mapping does not depend on the JIT, so it needs no optimised test run.
- **The service carries the extra work.** Every place that turns input into domain values must go through the helper. A new input path that forgets it turns a bad value into a 500, not a 400. That fails safe, since nothing leaks, and the endpoint tests send invalid values to every endpoint.
- **Two mappings exist in the solution:** the web application's (ADR 0018) and the API's. They agree on 400, 404 and 409 for the application's types. They differ only in how a bare `ArgumentException` is treated.
- **Tests:** `ErrorHandlingTests` run the API in Production, Staging and Development, with a dependency that fails using a secret-looking message. They check that the message appears in the log but never in a response outside Development.
