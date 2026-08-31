> **Status: live working document,** written before Phase 6 begins and meant to be edited as work lands. Decisions remain owned by [`docs/engineering-decisions.md`](../engineering-decisions.md) and [`docs/adr/`](../adr/README.md). See [`master-plan.md`](master-plan.md) §6.6, §6.10, §8 and §10 for the reasoning this brief compresses.

# GroceryEasy — Phase 6 agent brief

**Scope:** performance and hardening, observability, bulk import, end-to-end tests, containers, deployment, and the documentation that presents all of it.
**Budget:** about 36 hours, weeks 12 to 14.
**Branch:** `feat/phase-6-hardening-deploy`.
**Depends on:** Phase 5 complete.

---

## 0. Read this before anything else

**Documentation is roughly eight percent of the effort and roughly forty percent of the outcome.** It is not "the last night". Budget the README, the animation and the diagram as real tasks with real hours, and do them **before** the optional performance work, not after. If the phase runs out of time, an under-optimized application with an excellent README beats a fast one nobody understands.

The ordering below reflects that. Tasks 6.7 through 6.10 — containers, deployment and documentation — are the ones that must land. Tasks 6.1 through 6.6 are the ones to trim against the cut list if time runs short.

---

## 1. Hard constraints

All earlier constraints still apply. In addition:

1. **Migrations are applied at release, never at application startup.** Continuous integration produces the script with `dotnet ef migrations script --idempotent` and uploads it as a reviewable artifact; deployment runs it as a separate step **before** the new image rolls. The runtime database user has no schema-modification permission. `/health/ready` already fails when migrations are pending, which is what makes a mismatched deployment fail its probe instead of half-working.
2. **Migrations are expand-then-contract.** Add a nullable column, backfill, make it non-null in a later release. Never a destructive rename in one step.
3. **`docker compose up` must work from a clean clone on a different machine, first try.** This is the single most-checked thing by anyone evaluating the repository, and it will be broken the first time. Finding and fixing that is part of the task, not a sign something went wrong.
4. **`legacy-node/` stays in the repository, frozen and tagged.** It is the before-picture the whole README argument rests on. Do not delete it during cleanup.
5. **No secret in any committed file, any image layer, or any workflow log.**

### Cut list, applied in this order if time runs short

Batch and expiry tracking · one-time-password phone login · bulk comma-separated-values import · extra Hangfire jobs beyond reservation release, outbox and slot generation · a Prometheus metrics endpoint · the third store and review moderation · payment reconciliation, then refunds · Playwright down to one specification · Azure Bicep infrastructure files.

**Never cut:** the README with the audit table and the decision records, `docker compose up` working from a clean clone, and the live demonstration link.

---

## 2. Task breakdown

### Task 6.1 — Caching (about 4 hours)

Read [`docs/concepts/caching-strategy.md`](../concepts/caching-strategy.md). `Microsoft.Extensions.Caching.Hybrid` gives a two-level cache — in-process first level, Redis second level — with built-in stampede protection and tag-based invalidation. Redis is already in the `core` Compose profile and `ConnectionStrings__Redis` is already in `.env.example`.

Cache the category tree for one hour, product list pages for 60 seconds, product detail for five minutes, and pincode serviceability for one hour. Invalidate by tag on every admin write.

**Two things not to cache.** Slot availability gets a very short time-to-live only, because booking races make invalidation the wrong tool there. And **the cart is never cached as the source of truth** — `carts` and `cart_items` in PostgreSQL is the truth. Redis holds only the derived header badge count. Say this in the caching document; "I put the cart in Redis" is a common answer with a data-loss failure mode.

**Acceptance:** an admin product edit is visible on the public product page immediately, proving tag invalidation works rather than the time-to-live merely expiring.

---

### Task 6.2 — Rate limiting (about 2 hours)

The built-in ASP.NET Core rate limiter: five requests per minute on login, forgot-password and pickup-code verification; a sensible global limit elsewhere; a separate limit on the payment webhook. Rate-limit rejections return ProblemDetails with `Retry-After`, in the same shape as every other error — the single error contract has no exceptions.

Persist DataProtection keys to PostgreSQL so tokens survive a container restart. Without this, every deployment invalidates every outstanding email-verification and password-reset link.

**Acceptance:** six rapid login attempts produce a 429 with a ProblemDetails body.

---

### Task 6.3 — Index review and the N-plus-one sweep (about 3 hours)

Run `EXPLAIN ANALYZE` over the queries that actually matter — product search, the product list with facets, the order list, the admin order board — and confirm the GIN index is used for full-text search and that no sequential scan appears on a large table. **Save the search query's output for the README;** almost nobody does this, which is exactly why it reads well.

Sweep for N-plus-one queries by turning on Entity Framework sensitive-data logging in development and reading the actual statements for each list endpoint. Fix by projecting inside the query.

Confirm that the server-enforced `MaxPageSize` of 50 holds `[L-18]`.

**Acceptance:** the `EXPLAIN ANALYZE` output is captured, and every list endpoint issues one query.

---

### Task 6.4 — Observability (about 4 hours)

OpenTelemetry instrumenting ASP.NET Core, `HttpClient`, Npgsql and Entity Framework Core, exported over OTLP to the Aspire dashboard container, which is already in the `full` Compose profile at port 18888 with `OTEL_EXPORTER_OTLP_ENDPOINT` already in `.env.example`.

**Custom business metrics** through `System.Diagnostics.Metrics`, and these are the maturity signal rather than CPU graphs:

- `groceryeasy.orders.placed`, **tagged by fulfilment type**, so the product differentiator is measurable
- `checkout.duration`
- `payment.failures`
- `reservations.expired`

**Acceptance:** a distributed trace of a `PlaceOrder` request showing the transaction, the gateway call and the outbox insert — capture it as an image for the README.

---

### Task 6.5 — Bulk import (about 6 hours, first of the cut-list items to save)

`CsvHelper`, two-phase: upload, parse into `import_rows`, **validate and preview with per-row errors**, then commit. A downloadable error file for the rejected rows. This is the most convincing piece of internal tooling in the project and it demonstrates well, which is why the cut list puts two cheaper items above it.

**Acceptance:** a file with three bad rows out of fifty previews the three errors, imports the other 47 on commit, and produces a downloadable error file.

---

### Task 6.6 — End-to-end tests (about 4 hours)

Playwright, **exactly three specifications**. More will rot.

1. Customer pickup: browse, add a loose item, see the pickup-versus-delivery comparison, book a slot, pay with Cash on Delivery.
2. Delivery with a Razorpay test card and a simulated webhook.
3. Staff processes an order through to pickup-code verification, asserting that the customer window updated over SignalR.

`.github/workflows/e2e.yml`, running **nightly and on `main`**, not on every pull request. Flaky end-to-end tests on pull requests destroy trust in continuous integration faster than anything else.

**Acceptance:** all three green in the workflow, not merely locally.

---

### Task 6.7 — Containers (about 4 hours)

`deploy/Dockerfile.api` — multi-stage from `sdk:10.0` to `aspnet:10.0-noble-chiseled`, non-root via `USER $APP_UID`, no shell in the final image, restore layered separately for caching, target under 120 megabytes.

`deploy/Dockerfile.web` — `node:22-alpine` build to `nginx:1.27-alpine`, with single-page-application fallback routing, brotli compression, immutable caching for hashed assets, `no-store` on `index.html`, and runtime environment injection through a generated `env.js` so one image works in every environment.

Add `api` and `web` services to `docker-compose.yml` in the existing profile scheme. Note the comment already in that file: `--profile full --wait` exits 1 because `minio-init` is a one-shot container, so wait on the API's `/health/ready` rather than on container state, and make the `api` service depend on `minio-init` with `service_completed_successfully`.

`.github/workflows/docker-publish.yml` pushing to the GitHub container registry.

**Acceptance:** `cp .env.example .env && docker compose --profile full up -d`, then the application is reachable with no manual step. **Test this on a different machine**, or at minimum in a fresh clone in a different directory.

---

### Task 6.8 — Deployment (about 4 hours)

Read [`docs/concepts/hosting-strategy.md`](../concepts/hosting-strategy.md). The free stack, composed so that cold starts stay off the parts a reviewer touches first:

| Piece | Where | Note |
|---|---|---|
| Single-page application | Cloudflare Pages | global content delivery network, no cold start, so the link always feels instant |
| Images | Cloudflare R2 | 10 gigabytes free, zero egress cost |
| PostgreSQL | Neon free tier | avoids Render PostgreSQL's 90-day expiry, which would silently kill the demonstration |
| Redis | Upstash free tier | |
| API | Render free Docker web service | the one cold start; keep it warm with a GitHub Actions cron pinging `/health/live` every ten minutes |

Mitigate the remaining cold start: the single-page application renders its shell and skeletons from the content delivery network immediately and shows a "waking the demo server" state on the first API call, so a cold start reads as loading rather than as broken. Put the demonstration credentials table above the fold so a reviewer knows what to click while it wakes.

Add `ResetDemoDataJob` at 03:00 India Standard Time in the demonstration environment, and say in the README that the data resets nightly.

Migrations run as a release step per the constraint at the top of this brief.

**Acceptance:** the deployed link works from a phone on mobile data, and the second visit after an idle hour still shows a loading state rather than an error.

---

### Task 6.9 — The README (about 5 hours)

In this order, because the order is the argument:

1. **One-line pitch and the hero animation, 30 seconds or less, above the fold.** Browse, add 500 grams of loose tomatoes, cart shows pickup ₹487 against delivery ₹526, book a 6 to 7 pm pickup slot, pay with a Razorpay test card, then a split screen: staff marks the order ready and the customer's screen live-updates with the pickup code. If someone watches only this, they should already believe you.
2. **Live demonstration link and a credentials table** — customer, store staff, platform admin — plus "demo data resets nightly at 03:00 India Standard Time".
3. **The problem, in your own words** — kirana stores against the quick-commerce chains, with pickup-or-delivery as the differentiator.
4. **Architecture diagram** — a C4 level 2 drawn in Excalidraw, because hand-drawn reads as made by a human who understands it, plus a Mermaid version so it renders inline on GitHub.
5. **Run it locally in two commands.**
6. **Engineering highlights, six bullets,** each linking to the *file* and the *test*. The concurrency test, the reuse-detection test and the tenant-isolation test are the three that carry the most weight — name them.
7. **What this replaced** — the condensed table from `docs/legacy-audit.md`.
8. **Technical decisions** — a table linking each choice to its decision record.
9. **What I would do next, and known limitations** — single instance, no SignalR backplane, no Elasticsearch, no self-serve onboarding. Naming your own limitations accurately is the strongest single seniority signal in a README.

**Acceptance:** someone who has never seen the project can watch the animation and read to the end in under five minutes and correctly describe what was built.

---

### Task 6.10 — Complete the decision record set (about 2 hours)

ADR-0011, migrations at release rather than at startup, paired with [`docs/concepts/database-migration-deployment.md`](../concepts/database-migration-deployment.md), moved from *Planned* to *Accepted*.

Then audit the whole set: `docs/adr/README.md` should have **no rows left in the Planned table**. Walk `docs/concepts/` and confirm every document still describes the code as it actually is — several were written before the code existed and some will have drifted. Confirm the decision index at the end of `docs/engineering-decisions.md` is complete, and update its Part 2 roadmap to mark every phase complete.

Update `docs/plans/README.md` so the index lists every phase brief.

**Acceptance:** no Planned rows remain; every concepts document has been read against the code and either confirmed or corrected.

---

## 3. Definition of done

1. A fresh clone **on a different machine**: `cp .env.example .env && docker compose --profile full up -d`, application reachable, no manual step.
2. The deployed demonstration link works from a phone on mobile data.
3. Playwright green in `e2e.yml` — all three specifications, in the workflow.
4. `EXPLAIN ANALYZE` output showing the GIN index in use is in the README.
5. A distributed trace image of `PlaceOrder` is in the README.
6. Rate limiting returns 429 with ProblemDetails; six rapid login attempts prove it.
7. Tag-based cache invalidation is proved by an admin edit appearing immediately.
8. Migrations run as a release step; the runtime database user has no schema-modification permission; `/health/ready` fails when a migration is pending.
9. The README has the hero animation, the live link, the credentials table, the C4 diagram, six linked highlights, the audit table, the decisions table, and honest limitations.
10. `docs/adr/README.md` has no Planned rows; every concepts document is reconciled with the code.
11. No secret in any committed file, image layer or workflow log.
12. `git diff v1-legacy-node -- legacy-node/` returns empty, and `legacy-node/` is still present and still tagged.

---

## 4. Known pitfalls

- **`docker compose up` from a clean clone will fail the first time.** Test it in a genuinely fresh clone on a different machine. Fixing that failure is the task.
- **Chiselled images have no shell,** so `docker exec ... sh` will not work for debugging. Know that before you need it at three in the morning.
- **Free-tier PostgreSQL providers sleep and, on some plans, expire.** Neon over Render PostgreSQL is chosen precisely to avoid the 90-day expiry silently killing the demonstration months later.
- **The keep-warm cron consumes free instance hours.** Render's 750 hours per month covers a ten-minute ping; a one-minute ping does not.
- **Playwright on continuous integration needs `--with-deps`** to install browser dependencies, and it needs the whole stack running. Budget more time than the specifications suggest.
- **Caching hides bugs.** Add caching *after* the index review, so `EXPLAIN ANALYZE` measures the real query rather than a cache hit.
- **The hero animation is harder than it looks.** Seeded data, window sizes, timing, file size. Budget a full two hours for it and record it last, when the application is stable.
- **Do not delete `legacy-node/` while tidying.** The entire README argument depends on the before-picture still being there.

---

## 5. Reporting back

Same protocol. For this phase specifically, state which machine the fresh-clone test was run on and what broke on the first attempt — that answer is more interesting than a claim that nothing did.
