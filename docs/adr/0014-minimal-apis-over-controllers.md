# 0014 — Minimal APIs with an `IEndpoint` convention, over MVC controllers

- **Status:** Accepted
- **Date:** 2026-09-01

## Context

ASP.NET Core offers two ways to expose HTTP endpoints. MVC controllers are the older and more widely known: a class per controller, attribute routing, model binding, action filters, and a well-understood testing story. Minimal APIs are lambda-based, route-first, and have no per-request action-selection machinery.

Minimal APIs are frequently dismissed as "for toy projects", on the grounds that a real application needs the structure a controller provides. That is worth taking seriously rather than waving away: an application with sixty endpoints and no convention really does become a `Program.cs` nobody can read.

## Decision

**Minimal APIs, with an `IEndpoint` convention supplying the structure.**

```csharp
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
```

Implementations are `sealed internal`, discovered by assembly scan, and registered by one `MapEndpoints()` call. Related routes are grouped with `MapGroup` — `/api/auth` carries the eight authentication endpoints — so the group prefix, its tags and any shared authorization are declared once.

The rules that keep it structured:

- One file per feature area, not one file per route. `AuthEndpoints.cs` holds the group and its handlers.
- Handlers are `private static` methods, not inline lambdas. A named method has a name in a stack trace and somewhere to hang an XML comment.
- `TypedResults` throughout, so response types flow into the OpenAPI document rather than being described by hand.
- An endpoint's only job is HTTP: read the request, dispatch a command, map the `Result` to a response. Anything else belongs in a handler. The authentication endpoints are the clearest case — they read and write the refresh cookie, which is an HTTP concern, and know nothing about how a token is rotated.
- Errors go through one `Result` to `IResult` extension, never a status code chosen at the endpoint. See [ADR-0005](0005-result-over-exceptions.md).

## Why not controllers

- **The machinery is cost without benefit here.** Action selection, model-binder providers and the filter pipeline are paid on every request for behaviour this API does not use. The measured throughput advantage of minimal APIs is real but secondary; the simpler failure modes are the actual gain.
- **Filters overlap with the pipeline that already exists.** Validation, logging and transactions are handled by the dispatcher's decorator chain, so it applies to a command however it is invoked — including from a background job in Phase 5. Action filters would apply only to HTTP, which means two mechanisms and a question about which one owns a concern.
- **A controller groups by accident.** It gathers actions that share a route prefix, which is not the same as sharing a reason to change. That cuts against the feature-slice organisation in [ADR-0003](0003-vertical-slices-inside-clean-architecture.md).
- **Convention-based discovery is a real advantage.** Adding an endpoint is adding a file; there is no registration step to forget.

Controllers would be the better call for a server-rendered application needing views and tag helpers, for a team already deep in an MVC codebase, or where model binding to complex nested forms is doing genuine work. None applies here.

## Consequences

**Positive**

- Endpoint files are short and read top to bottom: route, authorization, handler, response.
- Adding an endpoint touches one file.
- No hidden per-request machinery, so debugging a request means stepping through code that is visibly there.
- `TypedResults` keeps the OpenAPI document accurate, which matters because the TypeScript client in Phase 3 is generated from it and continuous integration fails when it goes stale.
- Grouping makes shared authorization declarative — `/api/users` requires authentication once, for the whole group.

**Negative**

- Less familiar to developers who have only written controllers, so there is a small onboarding cost.
- Fewer conventions means the ones chosen here have to be written down and enforced. An architecture test asserts every `IEndpoint` is sealed; the rest is review.
- Endpoints are not directly unit-testable the way a controller action is, because they are wired to the routing table rather than being a class with a constructor. This is deliberate — they are tested through `WebApplicationFactory` against the real pipeline, which is where their behaviour actually lives.
- Some libraries still assume controllers, and integrating one may need an adapter.

## Related

- [`docs/concepts/minimal-apis.md`](../concepts/minimal-apis.md)
- [`docs/concepts/mvc-controllers.md`](../concepts/mvc-controllers.md) — when controllers are still right.
