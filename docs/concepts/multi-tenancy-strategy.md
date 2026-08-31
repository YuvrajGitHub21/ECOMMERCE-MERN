# Multi-tenancy strategy: single database with a `store_id` discriminator vs. the alternatives

## The criteria that actually matter for this decision

GroceryEasy is one codebase serving multiple independent kirana stores — "help local stores get online" only means something if it's plural. The criteria that matter for **this specific tenancy shape**, not multi-tenancy in general:

- **Isolation must be structurally enforced, not developer-remembered.** A query that forgets to filter by store is the tenancy equivalent of the legacy app's missing ownership checks (`[L-05]`, `[L-16]`) — a correct-looking handler that silently leaks another tenant's data because nothing at the framework level stopped it.
- **Cost has to be proportional to actual scale.** The product plan (§Phase 2 of [`engineering-decisions.md`](../engineering-decisions.md)) has three demo stores. There is no compliance regime, no enterprise customer, and no measured noisy-neighbour problem to design against yet.
- **Retrofitting tenancy later is expensive; designing it in early is cheap.** Every table, every query, every migration written before tenancy exists has to be revisited once it does. That cost only grows with the size of the codebase at the time it's paid.
- **The failure mode of getting it wrong is severe and silent.** A tenancy bug isn't a crash — it's Store A's manager quietly seeing Store B's orders, discovered by a customer complaint or a security researcher, not a stack trace.

## The three standard multi-tenancy models

Worth knowing cold, independent of which one a given project picks — this is one of the most commonly interviewed SaaS architecture topics.

### (a) Single database, shared schema, tenant discriminator column

Every tenant's rows live in the same tables, in the same database, distinguished by a `tenant_id` (here, `store_id`) column present on every tenant-owned table. Isolation is enforced entirely at the **application query layer** — every `SELECT`, `UPDATE`, and `DELETE` must include `WHERE store_id = @current`. Cheapest to operate: one database to back up, one schema to migrate, one connection pool, and cross-tenant analytics (if ever needed) is a single query with a `GROUP BY store_id`. The cost is that isolation is only as strong as the code that filters — there is nothing at the storage layer stopping a buggy query from reading across tenants, so the discipline has to be pushed down into a mechanism the developer can't forget to use (see below).

### (b) Schema-per-tenant

One physical database, but each tenant gets its own schema/namespace (`store_1.orders`, `store_2.orders`, …), typically with the application switching the active schema per request via the connection's search path. Stronger isolation than (a) — a query that omits a filter simply can't see another tenant's tables, because they're not in scope — at moderate operational cost: migrations must run once per schema (N tenants means N migration applications), and a single connection pool now has to be schema-aware. The middle ground: real isolation gain, without paying for N separate database instances.

### (c) Database-per-tenant

Full physical isolation — each tenant gets a dedicated database (or even a dedicated server). The strongest security and compliance story available short of separate infrastructure entirely: a bug can't cross tenant boundaries because there is no shared storage to cross, backups can be taken and restored per tenant independently, and a "delete all of tenant X's data" request (GDPR-style) is a `DROP DATABASE`, not a `DELETE WHERE`. The cost is real and multiplies with tenant count: N databases to provision, back up, migrate, monitor, and scale, and connection pooling either explodes (one pool per tenant) or requires a pooling proxy layer. This is the model enterprise B2B SaaS reaches for when a contract demands "your data never shares a database with another customer's," or when one tenant's load pattern would otherwise degrade every other tenant's latency (the noisy-neighbour problem).

## Comparison at a glance

| Criterion | (a) Shared schema + discriminator | (b) Schema-per-tenant | (c) Database-per-tenant |
|---|---|---|---|
| Isolation strength | Application-enforced only | Storage-layer boundary | Full physical isolation |
| Operational cost (backup/migrate/scale) | One database, flat cost | Moderate — N schema migrations | High — N database instances |
| Blast radius of an isolation bug | Cross-tenant data leak | Contained by schema boundary | Not reachable |
| Cost to add a new tenant | Insert a row | Provision a schema | Provision a database |
| Cross-tenant analytics/reporting | Trivial (one query) | Requires cross-schema query or ETL | Requires cross-database ETL |
| Right scale | Handful to hundreds of tenants, no hard compliance driver | Dozens to low hundreds, moderate isolation requirement | Enterprise/regulated tenants, or genuine noisy-neighbour risk |
| Fit for GroceryEasy today (3 demo stores) | Correct | Overkill | Overkill |

## Why not each alternative — the technical case

### Single tenant (no discriminator at all)

- Contradicts the product thesis outright — a grocery platform for exactly one store isn't the product being built. Not a real contender, included only because it's the zero-tenancy baseline.

### Schema-per-tenant

- Genuinely the right call at a larger scale than this project operates at — real isolation gain over (a) without the full cost of (c).
- For three demo stores it buys isolation nobody is asking for: there's no compliance requirement, no external auditor, and no tenant contractually demanding "not in the same tables as your other customers."
- It has a real recurring cost even at small scale: every migration now applies N times instead of once, and the connection layer has to become schema-aware — overhead that pays for itself only once there's a real isolation or scale driver to justify it.
- **Where it would win:** a moderate number of tenants with a real (if not extreme) isolation requirement — an internal platform serving several business units, for instance, where "another team can't see our schema" matters but full database-per-tenant would be excessive.

### Database-per-tenant

- The strongest isolation and the correct answer once a tenant is a real enterprise customer with a signed contract requiring dedicated data storage, or once one tenant's traffic is large enough to degrade another's latency if they shared infrastructure.
- Neither driver exists here: three demo stores, seeded with ~120 SKUs each, with no independent traffic pattern to isolate from one another.
- The operational cost multiplies with tenant count in a way that's actively hostile to a solo, time-boxed project — N databases to migrate at release (see [`database-migration-deployment.md`](database-migration-deployment.md)), N sets of credentials, N backup schedules.
- **Where it would win:** exactly the scenario this project doesn't have — regulated data (health, finance), contractual isolation requirements, or tenants large enough that noisy-neighbour effects are measured, not hypothetical.

## The non-technical factor — stated separately, if one exists

None beyond what's already covered in [language-platform-choice.md](language-platform-choice.md) — tenancy design here is driven entirely by the technical criteria above, not by audience fit or cost.

## How isolation is actually enforced (not just assumed)

The dangerous version of model (a) is "every handler remembers to add `.Where(x => x.StoreId == currentStore)`." That's the same shape of failure as the legacy app's authorization gaps — a rule that exists only where someone remembered to write it, and eventually isn't (`[L-05]`, `[L-16]`).

GroceryEasy closes that gap with two EF Core mechanisms working together, from `ADR-0010` / `F5` in [`engineering-decisions.md`](../engineering-decisions.md):

- **A global query filter, applied by convention to every `ITenantEntity`.** EF Core lets a `DbContext` register a `.Where()` predicate against a mapped entity type that is automatically appended to **every** LINQ query issued against it — `Find`, `Where`, `Include`, `Any`, all of it — without the call site doing anything. Concretely: `modelBuilder.Entity<Order>().HasQueryFilter(o => o.StoreId == currentStore.Id)`, applied to every `ITenantEntity` type by a convention that runs once at model-building time rather than being hand-written per entity. A handler that writes `_db.Orders.Where(o => o.Status == "Pending")` — with no store filter at all — still only ever sees its own store's pending orders, because the filter isn't optional at the call site; it's baked into the query EF Core generates before it ever reaches Postgres.
- **A `SaveChanges` interceptor that stamps and locks `store_id`.** On `INSERT`, the interceptor sets `store_id` to the current tenant automatically — no handler ever sets it by hand, so there's no code path that can insert into the wrong store by typo. On any subsequent `UPDATE` that attempts to change `store_id`, the interceptor throws, because there is no legitimate business operation that moves an order or a product from one store to another; the only way that value should ever change is a bug or an attack, and both should fail loudly rather than silently succeed.

Together, these mean a *forgotten* filter is not a leak — it's redundant, because the filter is already there — and a *mismatched* insert is not a leak, because the value is never taken from request input in the first place.

**Explicitly out of scope for now:** self-serve onboarding, per-store subdomains, per-store branding, and billing — none of which change the tenancy model, all of which are product surface built on top of it later if the product ever needs them.

## In GroceryEasy

See `F5` in [`docs/engineering-decisions.md`](../engineering-decisions.md) and [ADR-0010](../adr/0010-single-db-multi-tenancy.md) for the formal record. The proof this actually holds is an integration test, not an assumption: a manager authenticated to Store A requesting Store B's order returns **404, not 403** — see [`authorization-strategy.md`](authorization-strategy.md) for the general reasoning behind that choice (the API never confirms that another tenant's record exists at all). The Phase 2 "done" criterion in the roadmap is exactly this test passing on a fresh container.

**Cost, stated honestly:** every query on a tenant-owned entity carries a filter, whether it needs one or not — a small, constant overhead on every request. Noisy-neighbour isolation is nonexistent: a runaway query from one store's admin can still consume database capacity shared with every other store, because they're all in the same instance. And because the filter convention is the *entire* isolation mechanism, a bug in the convention itself — an entity that forgets to implement `ITenantEntity`, say — is a cross-tenant leak, not a degraded experience. That's exactly why the isolation test is mandatory in CI rather than a nice-to-have.

## Interview questions

**Q: Why not database-per-tenant — isn't that the "correct" way to do multi-tenancy?**
It's the correct way once there's a real driver for it — a contractual isolation requirement or measured noisy-neighbour contention. Neither exists for three demo stores. Database-per-tenant multiplies every operational cost (backup, migration, scaling) by tenant count, which is the wrong trade for a project with no compliance story and a fixed time budget. It's the right call for a platform selling to enterprise customers who demand dedicated storage — not for this stage of this product.

**Q: What actually stops a developer from forgetting to filter by store — isn't "just remember to add `.Where()`" the same discipline problem as the legacy app's authorization bugs?**
That's exactly the failure mode being designed against, and it's why the filter isn't something a developer applies per query. EF Core's global query filter attaches the `store_id` predicate to the entity type itself, once, at model-building time — every query against that type gets it automatically, including ones that never mention `store_id` at all. A handler that forgets is still safe, because there's nothing to forget; the filter isn't optional at the call site.

**Q: What if the query filter itself has a bug — doesn't the whole model collapse?**
Yes, honestly — a bug in the filter convention (an entity that doesn't implement `ITenantEntity`, or a filter predicate that's wrong) is a real cross-tenant leak, because the filter is the entire isolation mechanism in a shared-schema model. That's the trade-off of model (a) versus schema- or database-per-tenant, where the boundary is structural rather than logical. It's exactly why the isolation test — Store A requesting Store B's data and getting 404 — is a mandatory CI gate, not a nice-to-have: it's the only thing standing between "the filter works" and "it silently doesn't."

**Q: Why 404 instead of 403 when a store's staff requests another store's order?**
See [`authorization-strategy.md`](authorization-strategy.md) for the general reasoning — the short version is that a 403 confirms the record exists and simply isn't accessible, which is itself information a cross-tenant attacker can use to enumerate other stores' order ids. A 404 gives no signal either way.

**Q: When would you actually reach for schema-per-tenant or database-per-tenant on a real project?**
Schema-per-tenant once there's a genuine isolation requirement that a query filter doesn't satisfy — internal teams sharing a platform, say — but not yet enough scale or compliance pressure to justify separate databases. Database-per-tenant once a tenant is large enough, regulated enough, or contractually demanding enough that "your data is in a shared database with other customers" is a disqualifying answer in a security questionnaire — common in healthcare, finance, or any enterprise B2B SaaS with a real procurement process.

**Q: Doesn't stamping `store_id` on insert via an interceptor rather than trusting the request body sound familiar?**
It's the same structural instinct as server-authoritative pricing (`D1`) — don't accept a tenant-defining value from the caller and hope it's right; derive it server-side from the authenticated context, and make any attempt to override it after the fact a hard failure instead of a silent success.
