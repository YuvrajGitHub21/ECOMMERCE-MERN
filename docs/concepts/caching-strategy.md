# Caching strategy: HybridCache vs. the alternatives

## The criteria that actually matter for this decision

Not "how do you cache things" in general — the properties that matter for a read-heavy catalogue running on more than one instance:

- **Concurrent cache misses for the same key must not all hit the database at once.** A hot key (a category listing, a facet-count aggregate) expiring during real traffic shouldn't turn into dozens of identical, simultaneous queries against Postgres for a value every one of them is about to compute the same way.
- **A cache has to stay correct across every running instance, not just the one that handled the write.** An invalidation that only clears one instance's cache leaves every other instance serving stale data indefinitely — worse the more instances there are.
- **Invalidation needs to be expressible as "everything related to X," not as a hand-maintained list of every key that happens to contain X's data.** A category's price change can affect a category page cache, a facet-count cache, and any product-list cache filtered to that category — enumerating all of them by hand is itself a bug waiting to happen.
- **The hottest data should avoid a network hop entirely when possible**, while still being consistent enough across instances for anything cacheable at all — which rules out relying purely on either extreme (pure in-process, or pure network-hop-per-lookup).

## Comparison at a glance

| Criterion | `HybridCache` (chosen) | `IDistributedCache` / raw Redis calls | `IMemoryCache` only |
|---|---|---|---|
| Stampede protection | Built in — concurrent misses for one key collapse into a single factory call | None — must hand-roll a distributed lock | Not applicable across instances; no protection within an instance without hand-rolling either |
| Consistent across instances | Yes — L2 (Redis) is shared | Yes, if used correctly, but no in-process tier | No — every instance has its own independent cache |
| In-process (L1) speed for hot keys | Yes | No — always a network round-trip | Yes |
| Invalidation granularity | Tag-based — one call clears every related entry | Manual — caller must know and delete every affected key | Manual, and only ever local to one instance |
| Extra infrastructure | Redis (already used for the cart header-badge summary, see below) | Redis | None |
| Shipped in | .NET 9+ (`Microsoft.Extensions.Caching.Hybrid`) | Long-standing | Long-standing |

## Why not each alternative — the technical case

### `IDistributedCache` used directly, or raw Redis client calls

The standard pattern looks like: check the cache, miss, compute the value, write it back. `var v = await cache.GetAsync(key); if (v is null) { v = await ComputeExpensiveThingAsync(); await cache.SetAsync(key, v); }`.

- **Nothing about that pattern is atomic across concurrent callers.** If a popular key expires, or is invalidated, at a moment of real traffic, every concurrent request observes the miss at essentially the same instant and every one of them independently runs `ComputeExpensiveThingAsync()` — all hitting Postgres simultaneously for a value they're all about to compute identically. This is **cache stampede** (also called "thundering herd" or "dog-piling"), and it hits hardest exactly where a cache is doing the most good: a hot key, under load, at the moment it needs recomputing.
- Fixing it with `IDistributedCache` alone means hand-building a distributed lock around the compute step — a `SET key value NX PX ...`-style pattern in Redis, plus a decision about what every other concurrent caller does while the lock is held (poll? block? serve a stale value?). That's real distributed-systems code to write, reason about, and test, for a problem `HybridCache` has already solved once.
- **Where it's genuinely fine:** low-traffic keys where two requests recomputing the same value at once is a rounding error, not a real cost. Most caches in a small app never see enough concurrent contention for this to matter — the risk is specific to GroceryEasy's read-heavy catalogue endpoints (category pages, facet counts) under any real traffic.

### `IMemoryCache` only, with no distributed layer

Correct and genuinely simplest for a single instance — no network hop, nothing extra to deploy.

- The problem is scoped exactly to anything that runs more than one instance of the API, which this project's hosting can do, and shouldn't be designed to silently break the moment it does. Each instance's `IMemoryCache` is private to that process.
- Concretely: an admin edits a product's price. The request lands on instance A, which correctly invalidates its own in-process cache. Instances B and C never hear about it and keep serving the stale price until their own TTL independently expires — a customer's experience now depends on which instance the load balancer happened to route them to, for as long as the TTL lasts. That's not a crash; it's a silent, time-boxed correctness gap, which is a worse failure mode precisely because nothing about it looks broken.
- **Where it's still used:** `HybridCache`'s L1 tier is, internally, an in-process cache shaped exactly like `IMemoryCache`. The point was never "never cache in-process" — it's "never let in-process be the *only* tier once correctness has to hold across more than one instance."

## Chosen: `HybridCache` — L1 in-process, L2 distributed (Redis)

Shipped in .NET 9 (`Microsoft.Extensions.Caching.Hybrid`), `HybridCache` layers two tiers behind one API (`GetOrCreateAsync`):

- **L1 (in-process)** is checked first — sub-microsecond, no network round-trip — for data hot enough to be requested repeatedly on the same instance within a short window.
- **L2 (Redis)** is checked on an L1 miss. This still avoids the database, and, critically, is **shared across every instance** — an L1 miss on instance B can still be satisfied from L2 without touching Postgres at all, as long as *any* instance populated it. Only an L2 miss actually invokes the factory delegate that queries the database.

**Stampede protection, mechanically:** `HybridCache` de-duplicates concurrent `GetOrCreateAsync` calls for the same key within a process. If fifty requests arrive during the same miss window, exactly one of them invokes the factory function; the other forty-nine `await` that same in-flight task and receive its result once it completes, rather than each independently issuing the underlying query. The database sees one query, not fifty, no matter how many concurrent callers asked for the same missing key at once.

**Tag-based invalidation, mechanically:** entries are written with one or more tags (`"category:dairy"`, say). `RemoveByTagAsync("category:dairy")` evicts every entry carrying that tag — across L1 and propagated through L2 to every instance — in one call, without the caller needing to enumerate every key that happens to hold dairy-related data. A price change on a dairy product can invalidate the category page cache, the facet-count cache, and any filtered product-list cache in one call, instead of the invalidation logic needing to know about every place dairy data might be cached.

## The non-technical factor — stated separately

`HybridCache` shipped recently (.NET 9), and using it correctly is a small, concrete signal of staying current with the platform rather than reaching for whatever's most familiar. That's a real but secondary factor — it doesn't change the technical case above, which holds regardless of how recently the API shipped: stampede protection and cross-instance consistency are needed here whether the mechanism providing them is new or a decade old.

## In GroceryEasy

See `F4` in [`../engineering-decisions.md`](../engineering-decisions.md) for the formal record, landing in Phase 6 alongside tag invalidation wiring, rate limiting, and the N+1 query sweep. This doc is the general L1/L2/stampede/tag-invalidation reference for [cart-storage-strategy.md](cart-storage-strategy.md), which applies the same L2-as-derived-cache pattern to the cart header-badge summary — Postgres remains the source of truth for the cart itself (`D6`), and Redis caches only the small, derived summary shown in the header, never anything an eviction could make wrong to lose.

**What deliberately does *not* go through this cache:** inventory/stock counts. `D2`'s reservation mechanism (`UPDATE inventory SET reserved = reserved + @qty WHERE on_hand - reserved >= @qty`) depends on reading the current, uncached row at the moment of a reservation attempt — a cache layer that can be even briefly stale is exactly wrong for a value whose correctness depends on being read at the instant of a write. Catalogue metadata (names, descriptions, category listings, facet counts) is cache-appropriate; live stock levels are not.

## Interview questions

**Q: What's cache stampede, concretely, and how does `HybridCache` prevent it?**
It's what happens when a popular cache key expires or is invalidated under real traffic: every concurrent request misses at once and every one of them independently recomputes the same value, all hitting the database simultaneously. `HybridCache` de-duplicates concurrent `GetOrCreateAsync` calls for the same key — only one caller's request actually runs the factory function; every other concurrent caller awaits that same in-flight result instead of triggering its own database query.

**Q: Why not just use Redis directly — isn't that enough since it's shared across instances?**
Being shared across instances solves one problem, not both. Raw `IDistributedCache`/Redis calls give you no protection against stampede — the check-miss-compute-set pattern isn't atomic across concurrent callers, so a hot key expiring under load still causes every concurrent request to recompute it independently. You'd have to hand-build a distributed lock around the compute step to fix that yourself. `HybridCache` gives you that de-duplication for free, on top of the same shared L2.

**Q: Why not just `IMemoryCache` — isn't Redis overkill for a project this size?**
It's fine for a single instance, but it silently breaks the moment there's more than one. An admin price edit invalidates the cache on whichever instance handled the request; every other instance keeps serving the old price until its own TTL independently expires — different customers see different prices depending on which instance they land on, and nothing about that looks like a crash. `HybridCache`'s L2 (Redis) tier is what keeps invalidation consistent across every instance, while its L1 tier still gives you `IMemoryCache`-speed reads for the hottest keys.

**Q: What does tag-based invalidation actually buy you over invalidating by key?**
It removes the need to know, in advance, every key that could be affected by a given change. A price update to a dairy product might affect a category page cache, a facet-count cache, and a filtered product-list cache — invalidating by key means maintaining that list by hand and keeping it in sync as new cached views are added. Tagging every related entry with `"category:dairy"` at write time means `RemoveByTagAsync("category:dairy")` clears all of them in one call, correctly, even as new cached views are added later.

**Q: What would you deliberately *not* put behind this cache in this app?**
Live inventory/stock counts. The reservation mechanism depends on reading the current row at the exact moment of a reservation attempt — `on_hand - reserved >= @qty` has to be evaluated against the real value, not a value that could be a few seconds stale. Caching is for read-heavy, tolerant-of-slight-staleness data like catalogue listings and facet counts; anything the concurrency-correctness story depends on reads straight from Postgres.
