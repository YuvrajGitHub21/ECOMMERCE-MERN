# 0005 — `Result<T>` for expected failures, exceptions for bugs

- **Status:** Accepted
- **Date:** 2026-09-01

## Context

A command handler fails for two very different reasons, and conflating them is how error handling rots.

Some failures are **part of the domain**. An email is already registered. A refresh token has expired. Stock ran out between adding to the cart and checking out. A slot filled up. None of these is a defect — they are outcomes the business rules explicitly describe, they happen in normal operation, and the caller needs to be told which one occurred so it can respond usefully.

Others are **defects or infrastructure failures**. A null argument. A violated invariant. The database is unreachable. A required configuration value is missing. Nobody planned for these; there is no sensible per-case response, and the correct behaviour is to fail loudly with enough detail to fix the cause.

The legacy system used exceptions for both, wrapped in a `catchAsyncError` helper that funnelled everything into one middleware. The result was that a wrong password and a dropped Mongo connection travelled the same path and produced responses the client could not tell apart, and the middleware's shape did not match what any client actually read (L-17).

## Decision

**Expected failures return `Result` or `Result<T>`. Bugs and infrastructure failures throw.**

`Result` carries an `Error` with a stable machine-readable `Code`, a human-readable `Description` and an `ErrorType` from a closed set: `Validation`, `Unauthorized`, `Forbidden`, `NotFound`, `Conflict`, `Failure`. A single extension in the API maps `ErrorType` onto an HTTP status code and renders RFC 9457 ProblemDetails — one shape for every error the system produces.

`Result<T>.Value` **throws** when accessed on a failure. That is deliberate: reading the value of a failed result is a programming mistake, not a runtime condition, so it gets the treatment programming mistakes get.

Concretely:

```
Return a Result                        Throw
─────────────────────────────────      ──────────────────────────────
Auth.EmailAlreadyInUse                 ArgumentNullException
Auth.InvalidCredentials                InvalidOperationException on a broken invariant
Auth.InvalidRefreshToken               NpgsqlException — database unreachable
OutOfStock, SlotFull  (Phase 4)        Missing Jwt:SigningKey at startup
PincodeNotServiceable (Phase 4)        Result<T>.Value on a failure
```

Domain aggregate methods are the one place both appear: an invariant violation inside an aggregate throws, because reaching it means a caller skipped a check that a `Result`-returning validator should have performed first.

## Consequences

**Positive**

- A handler's signature tells the reader it can fail, and how. `Task<Result<TokenPair>>` is a contract; `Task<TokenPair>` plus a comment is not.
- No control flow through exceptions, so the expected paths are cheap. Throwing is fast to write and slow to run, and login failures are not rare.
- One place — `ResultExtensions` — decides what each failure means over HTTP, so an endpoint cannot invent its own status code for a condition another endpoint already maps.
- The error contract is generated into the TypeScript client in Phase 3, so client and server cannot drift. This is the permanent fix for L-17.
- A stack trace in a log now means something is genuinely wrong, rather than being the normal outcome of a mistyped password.

**Negative**

- Handlers carry explicit failure plumbing: check `IsFailure`, propagate, repeat. It is more verbose than letting an exception unwind, and it is the main cost of this decision.
- `Result.Failure<T>(other.Error)` conversions between result types are noise the language does not help with. A `Bind`/`Map` combinator set would reduce it and has been deliberately skipped for now — it adds an abstraction people have to learn before they can read a handler.
- The split is a judgement call at the edges, and two developers can disagree about which side a given failure belongs on. The table above exists to make the common cases non-negotiable.
- Nothing enforces it mechanically. A handler that throws for an expected failure still compiles and still returns a 500 through the global handler; only review catches it.

## Related

- [ADR-0014](0014-minimal-apis-over-controllers.md) — the endpoint convention that consumes `Result`.
- [`docs/concepts/result-pattern.md`](../concepts/result-pattern.md) — the pattern in general, and when it is the wrong choice.
- `docs/legacy-audit.md` L-17 — the defect this closes.
