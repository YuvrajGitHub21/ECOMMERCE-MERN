# Migration strategy: clean-room rewrite vs. the strangler fig pattern

## What the Strangler Fig pattern is

Martin Fowler's term (2004, describing an approach popularized by the "strangler fig tree" — a vine that grows around a host tree, gradually taking over its structure until the original can be removed and the fig stands on its own). Applied to software: instead of rewriting a system in one pass, you put a **facade or routing layer** in front of the legacy system, then migrate functionality **piece by piece** behind it. Each migrated piece gets routed to the new implementation; everything not yet migrated keeps hitting the old system, unchanged. Over time the legacy system does less and less until it does nothing, at which point it's deleted.

The reason it's the textbook-correct default: it never requires a big-bang cutover. At every point in the migration, the system in front of the facade is fully working — some requests served by new code, some by old, but nothing is ever *down* for the migration to happen. This is the pattern's entire value proposition, and it comes at a real cost: the facade itself, plus (usually the hardest part) making the new and old systems agree during the overlap — shared or bridged auth, and data that has to stay consistent across two stores if both are read from during the transition.

## The criteria that actually matter for this decision

- **Whether there is live traffic that cannot be dropped.** This is the single condition that justifies paying for a facade and a migration seam at all. Strangler fig exists to solve *this* problem specifically — not "the codebase is old" or "the language is different."
- **What a dual-write or dual-read window actually costs to build correctly**, given the specific stacks on each side (auth token formats, database engines) — not a hand-wave "some integration work."
- **What the true objective of the rewrite is**, and whether partial/incremental migration serves that objective or just defers it.
- **Budget as a hard constraint**, not an abstract preference — a fixed number of hours where every week spent on migration scaffolding is a week not spent on the thing the project needs to demonstrate.

## Comparison at a glance

| Criterion | Clean-room rewrite (chosen) | Strangler fig behind a gateway | Incremental in-place refactor |
|---|---|---|---|
| Requires live-traffic continuity | No — none exists | Yes, but none exists here either | Yes, but none exists here either |
| Extra infrastructure needed | None | Reverse proxy (e.g. YARP), route table, second deploy target | None |
| Auth during transition | Not applicable — one hard cutover | Cross-stack bridge (`jsonwebtoken` ↔ `JwtBearer`), a real integration risk | N/A — stays on the same stack |
| Data consistency during transition | Not applicable — one-time migration, one direction | Dual-write or split read/write routing between two databases | N/A — same database throughout |
| Reaches the target platform (ASP.NET Core) | Yes, immediately as the whole point | Yes, eventually, after the full endpoint-by-endpoint migration | **No** — stays on Node |
| Incremental, user-visible delivery | No — nothing ships until cutover | Yes — this is the pattern's main selling point | Yes |
| Estimated cost for this project | Absorbed into normal build time | ~3–4 weeks of scaffolding whose only purpose is to be deleted | Doesn't reach the objective, so cost is moot |

## Why not each alternative — the technical case

### Strangler fig behind a gateway (YARP)

The textbook-correct pattern for migrating a live system, and the most important one to get right in an interview — because the honest answer here is "usually yes, not this time," not "no, always."

- Its entire value is protecting **live traffic that cannot be dropped**. This system has zero users and a demo dataset of ~20 seed products. There is no traffic to protect, so the pattern is solving a problem that doesn't exist in this instance.
- The concrete cost of applying it anyway, estimated honestly: a YARP gateway project and route configuration (~1 week), a cross-stack auth bridge sharing token validity between Node's `jsonwebtoken` and ASP.NET Core's `JwtBearer` — or dual-issuing cookies on a shared parent domain — (~3–5 days, plus a class of bugs that exists only during the overlap window), and dual-write or per-context read/write routing between MongoDB and PostgreSQL (~1–2 weeks, the single highest-risk piece of work in the whole migration).
- That totals roughly 3–4 weeks — about a quarter of a ~170-hour budget — spent building scaffolding whose entire purpose is to be deleted once the migration finishes. None of it is a deliverable; none of it is what a reviewer of the finished project would ever look at.
- **Where it would win, unambiguously:** any system with real users and revenue. If GroceryEasy had live traffic, this would not be a close call — strangler fig would be the only responsible choice, full stop, and the 3–4 weeks of gateway/bridge work would be the correct trade against the alternative (an outage, or a big-bang cutover with real users on the other side of it).

### Incremental in-place refactor of the Node app

The "why not just improve what you have, gradually" option — rejected fast, for a reason specific to this project's actual objective.

- It never reaches ASP.NET Core, which is the primary objective of the rewrite — not an incidental detail but the entire reason the project exists in its current form (see [language-platform-choice.md](language-platform-choice.md) for the full technical case for C#/.NET over modernizing Node in place).
- Even done well, it fixes the *symptoms* documented in the legacy audit without changing the *structure* that produced them — e.g. improving input validation in Express controllers doesn't give you a database-enforced `CHECK` constraint; it gives you another place someone has to remember to check.
- No scenario where this wins for *this* project, because the objective (demonstrate ASP.NET Core / PostgreSQL fluency) can't be reached by refactoring code that stays on Node/Mongo. It would be the right call for a team whose actual goal was "reduce risk in the existing stack" rather than "change stacks" — that's just not this project's goal.

## The non-technical factor — stated separately

None specific to this decision beyond the technical case above — the "no live traffic" condition is itself the deciding fact, not a preference. The closest thing to a non-technical cost is opportunity cost: the ~3–4 weeks strangler fig would have consumed were redirected into the inventory reservation engine, the transactional outbox, and multi-tenancy — the parts of the project a technical reviewer actually probes. That's a resource-allocation argument, not a "the pattern is wrong" argument, and it's why the ADR frames it as a trade rather than a rejection.

## In GroceryEasy

See `B1` in [`docs/engineering-decisions.md`](../engineering-decisions.md) and the full record, including the migration order that *would* have been used had strangler fig applied, in [ADR-0001](../adr/0001-clean-room-rewrite-over-strangler-fig.md). The "the app is never broken" requirement is preserved by a different seam than a reverse proxy: the legacy app is frozen at tag `v1-legacy-node` and stays runnable throughout; the new SPA replaces the public URL once, at the Phase 4 boundary, once catalogue, auth, and cart have reached parity.

## Interview questions

**Q: What is the Strangler Fig pattern?**
A migration pattern, from Martin Fowler, where you put a facade or routing layer in front of a legacy system and migrate functionality behind it piece by piece — some requests go to the new system, the rest still go to the old one — until the legacy system does nothing and can be deleted. Its value is that the system is never down for the migration; the trade is the cost of the facade and keeping old and new consistent during the overlap.

**Q: Isn't strangler fig always the "correct" answer for a legacy rewrite? Why didn't you use it?**
It's the correct answer for a system carrying live traffic that can't be dropped — that's the specific problem it solves. This system has no users and a demo dataset, so that condition doesn't hold. Applying the pattern anyway would have cost roughly a quarter of the project's budget — a reverse proxy, a cross-stack auth bridge, and a Mongo/Postgres dual-write — building scaffolding whose only purpose was to be deleted at the end.

**Q: What would the migration order have looked like if you had used strangler fig?**
Read-heavy, low-write, low-coupling endpoints first — product listing and detail, since they're unauthenticated and cacheable — then admin product CRUD, then auth as a single atomic fork point (since whichever service issues tokens, the other must accept them), then cart as a greenfield context, and orders last, behind a brief write freeze, since they touch products, users, inventory, and payments simultaneously. It's documented in the ADR specifically because understanding the pattern matters more than having used it.

**Q: What did you lose by not using it?**
Incremental, user-visible delivery — nothing shipped until the Phase 4 cutover, so if the project had stalled at week 8 there would have been a half-built API and a still-running old app with nothing usable in between. It's also a single, unrehearsed cutover event rather than a series of small reversible ones, and it forgoes hands-on experience with a pattern that's genuinely valuable in industry — partly offset by prototyping a YARP gateway as a one-evening artifact and documenting it as evaluated and rejected for this context.

**Q: Why not at least do an incremental in-place refactor of the Node app, if you didn't want the gateway complexity?**
Because it never reaches ASP.NET Core, which is the actual objective — this project exists specifically to demonstrate a C#/.NET rewrite, and no amount of refactoring Express controllers gets there. It would also only fix symptoms, not structure: better validation in a controller doesn't give you a database-enforced constraint, it just gives you another place to remember to check.

**Q: How do you decide whether a system has "live traffic that can't be dropped"? What's the actual threshold?**
It's not a hard number so much as a question of consequence: does anyone lose something real — data, money, availability — if the old system goes away abruptly? Here the answer was concretely no: zero registered users, and a catalogue of ~20 seed products that were the wrong domain anyway (laptops and shoes on a grocery app) and were being replaced regardless. A system with even a handful of real customers placing real orders would already fail that test.
