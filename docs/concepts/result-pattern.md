# Result pattern

## What it is

Represent an operation's outcome as a **return value** — `Result<T>`, either `Success(value)` or `Failure(error)` — instead of throwing an exception for outcomes that are a normal, expected part of the business flow. Sometimes called *Railway-Oriented Programming* (a term from Scott Wlaschin's F# community): picture success and failure as two parallel tracks a pipeline of operations runs along, switching permanently onto the failure track the moment any step fails, skipping the rest.

```csharp
public async Task<Result<Order>> Handle(PlaceOrderCommand cmd, CancellationToken ct)
{
    var stock = await _inventory.TryReserveAsync(cmd.Items, ct);
    if (!stock.Succeeded)
        return Result.Failure<Order>(ErrorType.OutOfStock);

    // ...
    return Result.Success(order);
}
```

## Result vs exceptions — where the line actually is

Exceptions are for the genuinely exceptional: bugs, invariant violations, a dependency that should always be reachable and isn't. They unwind the call stack (relatively expensive), and — critically — **a method's signature gives no hint that it can throw**, so callers can't be forced to handle the failure case.

A `Result<T>` return type puts "this can fail, and here's how" into the method signature itself, as data the caller has to look at. The tradeoff: C# has no exhaustiveness checking (unlike F#'s or Rust's pattern matching against a discriminated union/enum), so nothing stops a caller from reading `.Value` without checking `.IsSuccess` first. It raises the bar on discipline rather than removing the risk.

## Where else you'll see it

Native in languages with algebraic data types — Rust's `Result<T, E>`, F#'s `Result<'T,'TError>`, Haskell's `Either`. In C#, it's a hand-rolled or library-provided (`FluentResults`, `LanguageExt`, `ErrorOr`) convention layered on top of a language that doesn't have it built in — which is also why the discipline caveat above matters more in C# than in the languages where the pattern originates.

## In GroceryEasy

A deliberate split — see `ADR-0005` and `C5` in [`docs/engineering-decisions.md`](../engineering-decisions.md):

```
Result   ← OutOfStock, SlotFull, PincodeNotServiceable, CartEmpty, StoreClosed
throw    ← null argument, invariant violation, DB unreachable, misconfiguration
```

The test applied at each call site: *can the caller reasonably do something about this?* "Out of stock" is a normal outcome the UI must render nicely — not exceptional, and not worth a stack unwind. A null argument is a bug and should be loud. A single `Result → IResult` extension maps every `ErrorType` to an [RFC 9457 ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) response, so the API has exactly one error response shape everywhere, closing the client/server contract mismatch that made every legacy error toast render `undefined` (`L-17`).

## Interview questions

**Q: Why not just use exceptions for everything?**
Because "out of stock" or "slot full" are normal business outcomes the UI has to render as a first-class case, not a crash — throwing for them costs a stack unwind for something expected, and hides "this can fail" from the method's signature, where a `Result<T>` return type surfaces it explicitly.

**Q: What's the discipline risk with `Result<T>` in C# specifically?**
Unlike F# or Rust, C# has no compiler-enforced exhaustiveness check — nothing stops a caller from reading `.Value` on a `Result` without first checking `.IsSuccess`. It's a convention enforced by code review, not the type system.

**Q: How do you decide whether something is a `Result` failure or an exception?**
Whether the caller can reasonably act on it. Business-expected outcomes (stock, capacity, business hours) are `Result`s the UI renders; genuine bugs or unreachable dependencies are exceptions that should fail loudly.
