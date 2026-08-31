# MediatR

## What it is

A popular in-process mediator library for .NET, written by Jimmy Bogard. The core API is small:

```csharp
public record PlaceOrderCommand(Guid CartId) : IRequest<OrderResult>;

public class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand, OrderResult>
{
    public Task<OrderResult> Handle(PlaceOrderCommand cmd, CancellationToken ct) { ... }
}

// caller:
var result = await mediator.Send(new PlaceOrderCommand(cartId));
```

One `Send(request)` call is routed, via the DI container, to exactly one handler — the caller never references the handler type directly. It also supports:

- **`INotification`** — publish/subscribe, where one event can have *multiple* handlers (unlike `Send`, which is strictly one-to-one).
- **`IPipelineBehavior<TRequest,TResponse>`** — middleware that wraps every `Send` call, used for cross-cutting concerns like logging, validation, and transaction management, so individual handlers stay focused on business logic.

## Why people reach for it

- Decouples caller from handler — a Minimal API endpoint just sends a message, with no direct reference to a service class or its dependencies.
- Pairs naturally with [vertical slice architecture](vertical-slice-architecture.md): one command/query type + one handler = one slice, all in one folder.
- Centralizes cross-cutting concerns (validation, logging, transactions) in pipeline behaviors instead of duplicating them at the top of every handler.

## The licensing turn

MediatR moved to a paid commercial license starting at v13 (September 2025). AutoMapper, from the same author, followed the same path. FluentAssertions (different author, same trend) went commercial at v8. This matters concretely for any project — commercial or portfolio — that can't or won't take on a paid dependency for something this central to the request pipeline; silently pinning the last free major version is also a real option, but it reads as an oversight rather than a decision if it isn't stated explicitly.

## In GroceryEasy

Not used — see `ADR-0004` and `C4` in [`docs/engineering-decisions.md`](../engineering-decisions.md). Replaced with a **hand-rolled dispatcher** (~150 lines including pipeline behaviors), registered via [Scrutor](https://github.com/khellang/Scrutor) (assembly-scanning DI registration), with the pipeline built as a `Scrutor.Decorate<>` chain instead of MediatR's `IPipelineBehavior<>`.

This is deliberately framed as *using the mediator pattern without the specific paid library* — not as rejecting the pattern itself. The mediator/CQRS-dispatch shape is exactly the same as a MediatR-based codebase; only the plumbing underneath differs.

## Interview questions

**Q: What is MediatR?**
An in-process mediator library for .NET — a single `Send()` call routes a request object to exactly one handler resolved via DI, decoupling the caller from the callee. Also supports pub/sub notifications (multiple handlers) and pipeline behaviors for cross-cutting concerns.

**Q: Why didn't you use it in this project?**
It became a paid commercial license at v13 (September 2025), same as AutoMapper from the same author. Taking a paid dependency on this project was the wrong call, and silently staying on the last free version would read as an oversight rather than a decision. Hand-rolled the mediator pattern instead: ~150 lines, Scrutor for DI registration, `Decorate<>` for the pipeline.

**Q: Isn't hand-rolling a dispatcher risky?**
For the actual surface area needed — single dispatch plus a three-stage pipeline (validation, logging, transaction) — it's small and fully understood by the team that owns it. The tradeoff is less community documentation and fewer battle-tested edge cases than a library with years of adoption behind it.

**Q: What's the difference between MediatR and CQRS?**
Independent concepts that are usually paired. See [cqrs.md](cqrs.md).
