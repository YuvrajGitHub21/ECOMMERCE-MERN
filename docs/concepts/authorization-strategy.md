# Authorization strategy: resource-based handlers vs. route middleware

## The criteria that actually matter for this decision

Not "how do you check permissions" in general — the properties that matter given this project's actual failure history:

- **A missing check must be structurally caught, not discovered by luck or an audit.** The legacy app's authorization bugs weren't caused by not knowing how to write a permission check — every *other* admin route was correctly gated. One was missed, and nothing noticed until this audit (`[L-05]`).
- **The check has to answer "is this the right *instance*," not just "is this the right *role*."** "Does this caller have the Customer role" and "does this caller own *this specific* order" are different questions, and a mechanism that only answers the first one gives false confidence about the second.
- **The enforcement boundary has to be somewhere the caller cannot bypass.** Anything shipped to a browser — including a route guard — runs on hardware the attacker controls.
- **Cross-tenant/cross-owner failures must not leak information through their failure mode.** A 403 and a 404 both refuse access, but they don't refuse *the same amount of information*.

## Comparison at a glance

| Criterion | Resource-based handlers (chosen) | Attribute-based role checks only | Manual inline per-handler checks | Client-side (frontend) route guards |
|---|---|---|---|---|
| Answers "is this the right role" | Yes | Yes | Yes, if written | No — cosmetic only |
| Answers "is this the right *instance*" | Yes, by design — runs against the loaded resource | No — the attribute never sees the resource | Yes, if written correctly | No |
| Enforceable by construction | Yes — a named policy attached to the route/group | Only for coarse, resource-free role gates | No — depends on a developer remembering | No — fully bypassable |
| Caught in CI if omitted on a new endpoint | Yes — a `[Theory]` iterates every protected endpoint | No — an omission looks identical to a correct route | No — same blind spot | N/A, it isn't a security layer |
| Where it actually runs | Server, against the loaded resource | Server, before any resource is loaded | Server, wherever the `if` was written | Browser — attacker-controlled |
| Real legacy failure it maps to | Closes both rows to the right | Is exactly the mechanism `L-05` slipped through | Same failure shape as `L-05`, just hand-written | Is exactly `L-06` |

## Why not each alternative — the technical case

### Route-level / attribute-based role checks only

`[Authorize(Roles = "Admin")]` (or a route-group `.RequireAuthorization("Admin")`) answers a question about the *caller* — does this token carry this role — and it answers it before the handler has loaded anything. It has no way to express "is this user the owner of order #482," because the attribute never sees order #482; role-based authorization runs during routing, against the principal alone.

- **The legacy evidence is precise, not hypothetical.** `DELETE /reviews` shipped with `isAuthenticatedUser` middleware but no ownership check — so any logged-in user could delete any other user's review (`[L-05]`). Every *other* admin-guarded route in the same app was correctly wired with `authorizeRoles("admin")`. The mechanism wasn't broken; it was *absent from one place*, and a role-check-only model has no way to notice that a route which needs an instance-level check only got a role-level one, because both look identical in a code review skim — an `[Authorize]` attribute either is or isn't there, and nothing distinguishes "this route correctly needs only a role check" from "this route needs an ownership check that was never added."
- Extending role attributes to cover ownership means either inventing a role per relationship (there is no "OwnerOfOrder482" role — ownership is a per-row fact, not a per-user grant) or falling back to an imperative check inside the handler anyway, at which point the attribute is providing the *appearance* of enforcement for a case it doesn't actually cover.
- **Where it's still the right tool:** a check that really is role-only, with no resource dimension at all — `RequireAuthorization("StoreStaff")` on the admin dashboard shell, say, where there's no "whose dashboard" question to ask. GroceryEasy keeps attribute-based role gates for exactly that shape of check; resource-based handlers layer on top only where an owning entity exists.

### Hand-written per-handler ownership checks

The direct alternative to a framework abstraction: `if (order.UserId != currentUserId && !isStoreStaff) return TypedResults.Forbid();`, written inline in every handler that touches an owned resource.

- This isn't wrong in the small — it's arguably what a resource-based handler *reduces to* internally. The gap isn't that any single `if` is hard to get right; it's that there is no shared place enforcing that every relevant endpoint *has* one. A new endpoint that forgets the check compiles, runs, returns `200`, and is visually indistinguishable from a correct one in a diff.
- That is structurally the same failure as `L-05`: a correct pattern, applied by convention rather than by construction, with nothing checking that the convention was followed everywhere it needed to be.
- A resource-based `IAuthorizationHandler` doesn't eliminate that `if` — `OrderOwnerOrStoreStaff` still contains logic that looks a lot like it. What changes is that the check now has a *name* (a policy string, `"OrderOwner"`) that can be attached to a route declaratively and enumerated — which is exactly the hook a CI test needs and an inline `if` scattered through handler bodies doesn't give you.

### Client-side route guards as the enforcement boundary

- A React Router guard (`ProtectedRoute`) is a UX convenience — it skips rendering a screen the API is going to reject anyway. It is never a security boundary, because anything shipped to the browser runs under conditions the attacker fully controls: disable JavaScript, or skip the SPA entirely and call the API directly.
- `[L-06]` is what happens when a codebase quietly starts leaning on the frontend guard as if it *were* the boundary: `ProtectedRoute` read `isAdmin` from its own props, but `App.js` passed that prop to the outer `<Route>` instead of the one rendering `ProtectedRoute`, so React Router silently dropped it — all nine `/admin/*` route guards were `undefined`, and every admin page rendered for any logged-in customer. The backend still returned `403` on the underlying data, so this was information disclosure (empty admin shells, error toasts) rather than a data compromise — but the guard was doing *nothing at all*, silently, for nine routes at once, and nothing in the codebase would ever have surfaced that on its own.
- GroceryEasy keeps client-side guards, deliberately relabeled: they exist only to avoid flashing a screen that the API is about to reject, never as the thing making that rejection happen. The actual gate is server-side, and it's the one covered by CI (below).

## How this actually closes the gap in code

**Resource-based authorization** is the general ASP.NET Core mechanism for the distinction above: an `IAuthorizationHandler<TRequirement, TResource>` receives not just the current principal but the *specific resource instance* the request is acting on — the loaded `Order`, not just "an order." `OrderOwnerOrStoreStaff` calls `context.Succeed(requirement)` only when `resource.UserId == currentUser.Id` (or the caller is staff of the store that owns it); every other case falls through and the framework denies by default. `ReviewAuthorOrStoreStaff` is the same shape, closing `L-05` directly. This is backed by database constraints doing independent work, not just the application: `unique (product_id, user_id)` on reviews and an `order_id` foreign key requiring a verified purchase mean the *data* can't represent an authorless review even if the application-layer check were ever bypassed.

The structural piece that turns "we wrote the right check" into "CI proves the check is present everywhere it needs to be": a `[Theory]` test that iterates **every** registered protected endpoint — reflected off the same `IEndpoint` registry the app uses to map routes (`C3`) — and asserts that a plain, authenticated customer gets `403` (or `404`, see below) hitting someone else's resource. A newly added endpoint that forgets its authorization policy doesn't get quietly missed; it gets a `200` where the test expected a rejection, and the build goes red. This is the direct structural answer to `L-05` and `L-06`: both bugs were "a check that should exist doesn't," and neither codebase had anything that would have noticed. This one does, on every commit, forever, rather than on the day someone happens to audit the routes again.

### Why 404, not 403, for cross-tenant reads

A cross-store or cross-owner read — a store manager requesting another store's order, a customer requesting another customer's order — returns **404, not 403**. The reasoning is about what each status code *reveals*, not just what it denies:

- **403 Forbidden** says: *this resource exists, and you are not allowed to see it.* That's a confirmed existence oracle. An attacker probing sequential order IDs can distinguish "doesn't exist" (404) from "exists, isn't yours" (403) without ever seeing the contents — which is enough to enumerate how many orders another store has processed, at what rate, and to correlate IDs with timing, none of which should be visible to an unrelated party.
- **404 Not Found** says nothing beyond "there is no resource at this address *for you*." It is indistinguishable from the resource genuinely not existing, so it reveals nothing about another tenant's data at all.
- This is deliberately narrow: it applies to reads that cross a tenancy or ownership boundary, where existence itself is the sensitive fact. It does **not** apply to same-tenant, role-based rejections where there's no existence ambiguity to protect — a customer hitting a staff-only endpoint on their *own* store's data legitimately gets a `403`, because there's nothing being concealed by revealing that the endpoint exists and requires a different role.

## In GroceryEasy

See `E3` in [`../engineering-decisions.md`](../engineering-decisions.md) for the formal record. Closes [`L-05`](../legacy-audit.md#l-05) (any logged-in user could delete any review), [`L-06`](../legacy-audit.md#l-06) (all nine admin route guards inert due to a misplaced prop), and [`L-16`](../legacy-audit.md#l-16) (customers blocked from their own orders because the only gate present was an admin role check that happened to work by accident). The multi-tenancy isolation test described in [multi-tenancy-strategy.md](multi-tenancy-strategy.md) is the same 404-not-403 reasoning applied at the store level rather than the individual-resource level — read that doc for how the two interact when a request crosses both a tenant boundary and an ownership boundary at once.

## Interview questions

**Q: Why not just use `[Authorize(Roles = "...")]` everywhere — isn't that simpler?**
It's the right tool for checks that really are role-only, and GroceryEasy still uses it for those. The problem is anything with an owning entity: a role attribute can tell you the caller is a Customer, but it has no mechanism for asking "is this Customer the owner of *this specific* order," because it runs before the resource is even loaded. That gap is exactly how `L-05` shipped — every other admin route had the right role check; one route needed an ownership check instead and nothing caught that it didn't have one.

**Q: Isn't writing an `if (resource.OwnerId != currentUserId)` in the handler basically the same thing — what does the abstraction actually buy you?**
Functionally, yes, a resource-based handler contains logic that looks like that `if`. What changes is that the check now has a name — a policy — that can be declared on a route and, critically, enumerated. A `[Theory]` test walks every registered endpoint and asserts the expected rejection for a plain customer; that test has no way to "see" an ad hoc `if` scattered through handler bodies, but it can absolutely see a missing `RequireAuthorization("OrderOwner")` on a route. The abstraction is what makes the omission testable, not what makes the check itself smarter.

**Q: Give a concrete example of a legacy bug this specific design prevents.**
`DELETE /reviews` in the legacy app had authentication (`isAuthenticatedUser`) but no ownership check at all, so any logged-in user could delete anyone else's review (`L-05`). A `ReviewAuthorOrStoreStaff` resource-based handler makes that check a named, declared requirement on the route rather than a line that has to be remembered — and the CI `[Theory]` test means a *future* endpoint that makes the same mistake fails the build instead of shipping.

**Q: Why return 404 instead of 403 when a store manager requests another store's order?**
A 403 confirms the order exists and simply isn't visible to this caller — which is itself information: an attacker can enumerate which order IDs exist at all, at another tenant, without ever reading their contents. A 404 is indistinguishable from the ID not existing, so it reveals nothing. This is only for reads that cross a tenancy or ownership boundary — a same-tenant role failure, where there's no existence fact worth hiding, still returns a plain 403.

**Q: If the frontend admin guards don't provide real security, why keep them at all?**
Purely for UX — so a customer who somehow lands on `/admin/dashboard` sees a redirect instead of a screen that renders and then fails every data request. `L-06` is the cautionary tale for treating that guard as anything more: all nine of them were silently broken (a prop passed to the wrong nested route) and nobody noticed for a long time, because a broken UX-layer guard degrades gracefully into "shows an empty page," not into a loud failure. The real boundary is server-side and it's the one covered by the CI theory test.

**Q: Walk me through what happens if I add a new endpoint tomorrow and forget to add authorization to it.**
It compiles, it deploys to nothing yet, and the `[Theory]` test — which reflects over every `IEndpoint` in the assembly, not a hand-maintained list — hits it as a plain customer targeting someone else's resource. If the route has no policy attached, the request succeeds where the test expects a `403`/`404`, the assertion fails, and CI goes red before merge. That's the actual mechanism turning "a route can be forgotten" into "a forgotten route fails the build" — the same shape of fix as the multi-tenancy query filter in [multi-tenancy-strategy.md](multi-tenancy-strategy.md), applied to authorization instead of tenant isolation.
