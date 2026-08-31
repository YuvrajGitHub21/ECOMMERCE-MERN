# Cart storage: Postgres as source of truth vs. localStorage and Redis

## The criteria that actually matter for this decision

A shopping cart looks like a trivial piece of client UI state until it has to hand off to order creation — the criteria that actually decide where it should live:

- **Transactional participation.** The cart has to be read, locked if necessary, and cleared inside the *exact same database transaction* that creates the order — so the two can never disagree about whether checkout succeeded. Anything that can't participate in a Postgres transaction fails this outright, no matter how it otherwise performs.
- **Durability matching the data's actual value.** A grocery cart is often assembled across multiple sessions over hours or days, not built and spent in one sitting — losing it silently is a real customer-facing loss, not a minor inconvenience.
- **Cross-device and cross-session continuity.** A customer adding items on a phone and checking out on a laptop needs the cart to be addressable by their identity, not scoped to one browser's storage.
- **An anonymous → authenticated merge step.** A guest can add items before logging in; merging that guest cart into the user's cart on login requires the cart to already be a server-side object with an identity of its own.
- **Cheap reads for a value checked on nearly every page.** The header badge ("3 items") is read on almost every navigation. That specific read pattern — high frequency, low information content, tolerant of being briefly stale — is a different problem from "where does the cart actually live," and deserves a different answer.

## Comparison at a glance

| Criterion | Chosen: Postgres (cart) + Redis (cached badge count) | `localStorage` | Redis as source of truth |
|---|---|---|---|
| Participates in the order-creation transaction | Yes — `Cart.Clear()` runs in the same transaction as order creation | No — no server-side existence at all | No — a second system, not the transactional store |
| Survives a restart / eviction / failover | Yes — as durable as every other business record | Yes, on the same device, until the user clears it | Not guaranteed — eviction, restart, or a botched failover can silently drop a key |
| Cross-device continuity | Yes — tied to the user's identity server-side | No — tied to one browser profile on one device | Yes, until it isn't (see above) |
| Anonymous → user merge | Straightforward — both are server-side rows | Not naturally expressible | Straightforward, until data loss makes it moot |
| Latency for the common add/remove operation | One API round trip (already happening in an authenticated SPA) | Instant, no round trip | One round trip, but to a cache, not a system of record |
| Failure mode when something goes wrong | A normal DB error, visible and retryable | Cart silently diverges from anything the server knows | Cart silently vanishes, with no error surfaced to anyone |

## Why not `localStorage`

The legacy app's actual design, and the direct cause of `[L-15]`.

- **No server-side existence at all.** The cart lives purely in the browser's storage, which means it cannot participate in a database transaction with order creation — "order was created" and "cart was cleared" are two independent operations with no atomicity between them. That gap is exactly what `L-15` was: checkout dispatched `createOrder` without awaiting it and navigated to a success screen regardless of the outcome, with no `CLEAR_CART` action anywhere in the codebase — going back and resubmitting created a second identical order at a stale total.
- **No cross-device continuity** — a cart tied to one browser's storage doesn't exist on a second device, and nothing server-side knows a cart exists until checkout is attempted.
- **No server-side visibility** into cart state for anything beyond the browser that built it — abandonment handling, admin support ("what was in this customer's cart"), or a future soft-hold on inventory ahead of checkout are all foreclosed by design.
- **Where it would genuinely win:** zero infrastructure and zero server round trips for the single most frequent cart operation — add/remove an item — with instant UI feedback. This is a real, common design in production storefronts: an optimistic client-side cart, only materialized server-side at the moment checkout begins. It's a legitimate hybrid a larger team might reasonably choose. It reintroduces exactly the synchronization window this project is structured to close, though, and GroceryEasy's SPA is already making authenticated API calls for live pricing and stock, so the latency argument for keeping the cart purely client-side doesn't move the needle here the way it might for a lighter-weight storefront.

## Why not Redis as the source of truth

The tempting middle ground — "server-side, but fast" — and the one that needs the most careful argument against, because it *feels* like it solves the `localStorage` problem.

- **Redis is architecturally a cache, even with persistence turned on.** AOF or RDB persistence narrows the window of possible loss; it does not eliminate it. An eviction under `maxmemory-policy` pressure, a node restarting before its last fsync, or a failover in a managed Redis (Upstash, in this project's hosting — see `I2`) can drop a key with **no error surfaced to anyone** — the operative word being *silently*. A lost cart doesn't throw an exception; it's simply empty the next time the customer looks.
- **Even durable configurations accept it by design.** `appendfsync everysec` (a common durability setting) accepts up to roughly a second of possible data loss on crash, deliberately, as the right trade-off for a cache or an ephemeral session store. That trade-off is correct for state that's cheap to lose or rebuild — it's the wrong trade-off for a customer's actual, sometimes multi-session purchase intent.
- **It still doesn't solve the transactional-participation criterion.** A cart in Redis still can't be read, cleared, and committed inside the same Postgres transaction that creates the order — the fundamental gap is identical to `localStorage`'s, it just *feels* more solved because Redis is a server-side system. "I used Redis for the cart" is a common answer in interviews, and it has a real, disqualifying failure mode: an eviction or restart quietly loses a customer's cart, and nothing in the system would even notice.
- **Where it would genuinely win:** state that's actually ephemeral and cheap to rebuild — a session-scoped cart with no cross-session requirement, or a workload where sub-millisecond reads at very large scale are the actual bottleneck. That's not this product's shape: a grocery cart can be a twenty- or thirty-item list built up over multiple visits, which is precisely the kind of state a cache is the wrong home for.

## The chosen design — source of truth vs. a cache of a derived value

**Postgres holds the cart** — `cart_items` rows, keyed to the cart owner, each a foreign key to a product variant plus a quantity — as the actual system of record. It is exactly as durable as every other piece of business data in this system: it survives restarts, deploys, and failovers because it's WAL-backed and replicated, not because it's a best-effort cache that happens to persist. Critically, it can be read and cleared **inside the same transaction that creates the order** (`Cart.Clear()` runs in that transaction — see `D3`), so "the order was placed" and "the cart was emptied" can never disagree with each other.

**Redis caches only the derived header-badge count** — a single integer, "3 items in cart," read on nearly every page navigation. This is the general caching-strategy distinction worth stating plainly: a **cache of a derived value** stores something computed *from* the source of truth, purely to avoid recomputing it on every read, and can be deleted at any moment with zero data-loss consequence — the next read just recomputes it from Postgres and re-populates the cache. A **primary store of truth** is where a piece of information exists at all; if it vanishes, that information is gone. The test that tells them apart: *if this value disappeared right now, is that a cache-miss or a data-loss incident?* For the badge count, it's a cache-miss — one indexed `COUNT` query away from being right again. For the cart's actual contents, it would be a data-loss incident — which is exactly why they don't live only in Redis. See [caching-strategy.md](caching-strategy.md) for the general pattern.

## In GroceryEasy

See `D6` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record. Closes [`L-15`](../legacy-audit.md#l-15) — a `localStorage` cart with no server-side existence, no `CLEAR_CART` action, and a fire-and-forget checkout call that let a customer land on a success screen and then resubmit into a second order.

## Interview questions

**Q: Why not just keep the cart in `localStorage` like a lot of SPAs do?**
Because it has no server-side existence, so it can't participate in a transaction with order creation — "order created" and "cart cleared" become two unsynchronized operations, exactly the gap that produced `L-15` in the legacy app: no `CLEAR_CART` action existed at all, and a customer resubmitting a stale cart created a duplicate order. It also can't follow a customer across devices or support merging a guest cart into an account on login.

**Q: Isn't Redis basically a database too, especially with persistence turned on?**
Persistence narrows the loss window, it doesn't close it — an eviction under memory pressure, a restart before the last fsync, or a failover can silently drop a key, with nothing in the system surfacing an error. That's an acceptable trade-off for a cache; it's not acceptable for a customer's actual cart contents, which can represent a multi-session shopping list built up over real effort. Redis as cart storage also still can't participate in a Postgres transaction with order creation — it just feels more solved than `localStorage` because it's server-side.

**Q: What's actually in Redis for the cart, then, and what happens if that key disappears right now?**
Only a derived integer — the header badge count, "3 items in cart" — never the cart's actual contents. If that key disappears, the next page load is a cache-miss: one indexed `COUNT` query against Postgres recomputes it and re-populates the cache. Nothing is lost, because nothing that mattered was only in Redis to begin with.

**Q: What's the general principle behind splitting it this way?**
A cache stores a value *derived from* a source of truth, purely to avoid recomputing it on every read; a primary store is where the information actually lives. The test is whether losing the value right now is a cache-miss or a data-loss incident. Applying that test consistently is what decides, for any given piece of state, whether Redis is an acceptable home for it or not — see [caching-strategy.md](caching-strategy.md).

**Q: Would `localStorage` ever be the right call for a cart?**
For a lighter-weight storefront with no cross-device requirement and no need for the cart to interact transactionally with anything else, an optimistic client-side cart materialized server-side only at checkout is a legitimate, commonly used design — it trades the transactional guarantee for zero server round trips on the most frequent operation. It's not the right trade here, because this project is specifically structured around closing the class of bug that trade-off reopens.
