# Minimal API pattern

## What it is

Introduced in .NET 6: define an HTTP endpoint as a delegate mapped directly on the app builder, with no controller class and no `[ApiController]` attribute in between.

```csharp
app.MapPost("/orders", async (PlaceOrderCommand cmd, IDispatcher dispatcher, CancellationToken ct) =>
{
    var result = await dispatcher.Send(cmd, ct);
    return result.ToHttpResult();
});
```

Parameters are bound from the route, query string, body, or DI container based on their type and attributes. There's no MVC filter pipeline underneath — cross-cutting behavior is added through **endpoint filters** (`.AddEndpointFilter(...)`) instead of action filters.

## Where else you'll see it

The same "map a function directly to a route" style is the default in Express (Node), FastAPI (Python), and Go's `net/http` — Minimal APIs is ASP.NET Core adopting that shape after two decades of the heavier MVC-controller convention it inherited from ASP.NET MVC/Web API.

## In GroceryEasy

Wrapped in a small `IEndpoint` convention rather than used raw:

```csharp
public interface IEndpoint { void MapEndpoint(IEndpointRouteBuilder app); }
```

One type per endpoint, auto-registered by assembly scan, living in its feature's [vertical slice](vertical-slice-architecture.md) folder beside the command, handler, and validator. See `ADR-0014` and `C3` in [`docs/engineering-decisions.md`](../engineering-decisions.md).

Why, over [MVC controllers](mvc-controllers.md): a controller groups endpoints by *resource* (`OrdersController` accumulating `PlaceOrder`, `GetMyOrders`, `CancelOrder`, each needing a different, unrelated set of injected dependencies dragged through one shared constructor). A vertical slice groups by *use case* instead — those are different axes, and having chosen vertical slices, controllers would immediately fight that decision.

Concrete payoffs: dependencies are injected per-endpoint rather than through a shared constructor; `TypedResults` gives compile-checked response types that flow automatically into the generated OpenAPI document, which is what makes the generated TypeScript client trustworthy and permanently closes the client/server contract mismatch that made every legacy error toast display `undefined` (`L-12`); and there's no controller activation or MVC filter pipeline overhead per request.

Cost: the `IEndpoint` registration convention has to be written and owned (~30 lines) or `Program.cs` becomes a wall of `app.MapPost(...)` calls; endpoint filters are less capable than mature MVC action filters, pushing cross-cutting concerns into the Application-layer pipeline instead — which the project considers an acceptable, arguably correct, place for them to live.

## Interview questions

**Q: What's the actual mechanical difference from MVC controllers?**
No controller class or activation, no `[ApiController]` pipeline, no built-in model-state auto-validation, no action filters — validation and cross-cutting concerns move to endpoint filters or an application-layer pipeline instead. Slightly lower per-request overhead as a result.

**Q: Why choose Minimal APIs here specifically?**
Because the project organizes code by [vertical slice](vertical-slice-architecture.md) (by use case), and MVC controllers naturally group by resource — those are different axes, and controllers would fight the slice structure. `TypedResults` also gives compile-checked responses that flow into the OpenAPI-generated TypeScript client.

**Q: When would Minimal APIs be the wrong choice?**
A large, CRUD-heavy, resource-oriented surface where `[ApiController]`'s automatic model validation and the mature MVC action-filter ecosystem save real time, or a team with deep controller-based muscle memory and no appetite for building the endpoint-registration convention.
