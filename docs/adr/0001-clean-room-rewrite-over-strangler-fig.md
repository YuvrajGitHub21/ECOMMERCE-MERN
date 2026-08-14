# 0001 — Clean-room rewrite over strangler-fig migration

- **Status:** Accepted
- **Date:** 2026-08-15
- **Related:** [0002](0002-postgresql-over-mongodb.md), [`docs/legacy-audit.md`](../legacy-audit.md)

## Context

The existing system is a Node/Express + MongoDB API with a Create React App frontend, carrying [20 documented defects](../legacy-audit.md) including forgeable order totals, a simulated payment flow, and an inventory decrement that can terminate the process.

The backend is being rewritten in ASP.NET Core over PostgreSQL. The question is *how* to get from one to the other.

Facts that bear on the decision:

- The system has **no users and no revenue**. It is a portfolio project with a demo dataset.
- The catalogue is ~20 seed products whose images are base64 blobs of laptops, cameras and shoes — wrong-domain data for a grocery app, which will be replaced regardless.
- **100% of the backend is being replaced** (3 models, 4 controllers, ~800 lines) and **100% of the frontend** (language, state management and styling all change at once).
- The budget is ~170 hours of evening work.

## Options considered

### A. Strangler-fig migration behind a YARP gateway

Stand up a reverse proxy in front of both stacks, migrate endpoint by endpoint, and retire the Node app once nothing routes to it.

Costs, estimated honestly:

| Work | Estimate |
|---|---|
| YARP gateway project, route config, extra deploy target | ~1 week |
| Cross-stack auth bridge — sharing an HS256 secret between `jsonwebtoken` and `JwtBearer`, or dual-issuing cookies on a shared parent domain | ~3–5 days, plus a class of bugs that exists only during the overlap |
| Dual-write or per-context read/write routing between Mongo and Postgres | ~1–2 weeks, and the highest-risk work in the project |

That is **3–4 weeks — roughly a quarter of the total budget** — building scaffolding whose entire purpose is to be deleted.

### B. Clean-room rewrite with a one-time data migration

Build the new system alongside the frozen old one. Cut over once at the end.

### C. Incremental in-place refactor of the Node app

Rejected quickly. It does not reach ASP.NET Core, which is the primary objective — the target employer's stack, and the thing that makes this project worth defending in an interview.

## Decision

**Option B.** Clean-room rewrite, one-time data migration, single hard cutover.

The strangler-fig pattern solves exactly one problem: migrating a system carrying **live traffic that cannot be dropped**. That problem does not exist here. Applying the pattern anyway would spend a quarter of the budget protecting users who do not exist, and would leave behind an artifact (a YARP route table) that nobody will ever ask about — while the four weeks it consumed would otherwise have gone into the inventory reservation engine, the transactional outbox and multi-tenancy, which are exactly what a reviewer *does* probe.

**The requirement "the app is never broken" is preserved by a different seam — the frontend, not a reverse proxy:**

- The entire legacy app is frozen at tag `v1-legacy-node` and remains runnable for the whole migration. It is never edited again.
- The new SPA is built greenfield and replaces the public URL only at the Phase 4 boundary, once catalogue, auth and cart have reached parity.
- **Auth is not shared.** Users re-authenticate once at cutover. This is free, because there are no users.
- **Data is not kept consistent across both stacks**, because it does not need to be. One migration, one cutover, one bounded context.

### The migration order we *would* have used

Recorded because understanding the pattern matters more than using it. With real traffic, migrate read-heavy, low-write, low-coupling endpoints first, then climb the write-risk ladder:

1. `GET /products`, `GET /product/:id` — read-only, cacheable, unauthenticated. Proves the proxy and the serialization contract with nothing at stake.
2. Admin product CRUD — writes, but one aggregate with no cross-context reads.
3. **Auth** — the fork point. Whichever service issues tokens, the other must accept them, so this moves as one atomic step, never endpoint by endpoint.
4. Cart — a new bounded context with no legacy equivalent; greenfield, nothing to migrate.
5. **Orders — last.** They touch products, users, inventory and payments simultaneously. Migrated as a hard cutover behind a brief write freeze, never a dual-write.

## Consequences

**Positive**
- ~3–4 weeks redirected from disposable scaffolding into the correctness work that carries the project.
- No dual-write, which removes the single highest-risk workstream.
- The new system is designed from its invariants rather than shaped around a legacy contract it must temporarily satisfy.
- `v1-legacy-node` stays runnable as an honest before-picture, which the audit document depends on.

**Negative**
- No incremental value delivery: the new stack ships nothing user-visible until Phase 4. If the project stalls at week 8, there is a half-built API and a still-running old app, and nothing in between.
- The cutover is a single, unrehearsed event rather than a series of small reversible ones.
- Forgoes hands-on experience with a migration pattern that is genuinely valuable in industry. Partially offset by prototyping a YARP gateway as a one-evening artifact in Phase 6 and documenting it here as *evaluated and rejected for this context*.
- Two frontends coexist in the repository for several weeks, which is briefly confusing to a reader.
