# MVC controller pattern

## What it is

ASP.NET Core's older, more established API style: a class deriving from `ControllerBase`, decorated with `[ApiController]`, where each public method is an *action* mapped to a route via attribute routing.

```csharp
[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;
    private readonly IPricingEngine _pricing;
    private readonly INotificationService _notifications;
    // ...

    [HttpPost]
    public async Task<IActionResult> PlaceOrder(PlaceOrderRequest req) { ... }

    [HttpGet("mine")]
    public async Task<IActionResult> GetMyOrders() { ... }
}
```

`[ApiController]` brings real, automatic behavior: model-state validation runs before the action executes and returns a `400` on its own, binding source inference is smarter, and problem-details responses are wired in by default. **Action filters** (`IActionFilter`, `IAsyncActionFilter`) are a mature, well-documented extension point for wrapping actions with cross-cutting logic.

## Strengths

- Every .NET developer already knows the convention — near-zero onboarding cost.
- `[ApiController]` auto-validation and auto-`400` save real boilerplate.
- Action filters are a mature, battle-tested extension point.
- Naturally fits resource-oriented REST design — one controller per resource, HTTP verbs as CRUD actions.

## The structural tension with vertical slices

A controller groups its actions by **resource** (`OrdersController` holds every operation on orders). A [vertical slice](vertical-slice-architecture.md) groups by **use case** (`PlaceOrder`, `GetMyOrders`, `CancelOrder` as separate, independent units). Those are different axes: as a controller accumulates actions, its constructor accumulates the union of every dependency any action needs, even though `PlaceOrder` needs the pricing engine and `GetMyOrders` needs none of it — each action drags the other actions' dependencies along for the ride. The class becomes a unit of grouping with no real relationship to how the code actually changes day to day.

## In GroceryEasy

Considered and not used, in favor of [Minimal APIs](minimal-apis.md) — not a categorical rejection of MVC as a pattern, but a direct consequence of choosing vertical slices as the organizing principle for `Application`. See `ADR-0014` and `C3` in [`docs/engineering-decisions.md`](../engineering-decisions.md).

## Interview questions

**Q: When is the MVC controller pattern still the right call?**
Resource-oriented, CRUD-heavy APIs; teams already fluent in the convention; or when `[ApiController]`'s automatic model-state validation is worth more than building an equivalent yourself. It remains the more mature, more widely-documented option.

**Q: What's an action filter, and how does it compare to a Minimal API endpoint filter?**
Both wrap request/response handling around the actual handler for cross-cutting concerns (logging, auth, exception translation). Action filters are older, more capable, and better documented; endpoint filters are the Minimal API equivalent and currently thinner.

**Q: Why did this project not use MVC controllers, specifically?**
Not a rejection of MVC's merits — it's a direct consequence of choosing to organize the codebase by use case ([vertical slices](vertical-slice-architecture.md)) rather than by resource. A controller's per-resource grouping fights that structure by design.
