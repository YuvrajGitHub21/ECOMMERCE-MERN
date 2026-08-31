# Phase gates — how each phase is checked

One page, so that verifying a phase never depends on who built it or on what they claim. Every gate below is a command with an expected result, or a manual check with a stated observable outcome. Nothing advances until its gate is green **in continuous integration**, not only on the machine that wrote it.

Run everything from the repository root.

---

## Checks that apply to every phase

Run these first, every time, before looking at anything phase-specific.

```bash
# 1. The frozen legacy app is untouched. Must print nothing.
git diff v1-legacy-node -- legacy-node/

# 2. Clean build, zero warnings.
dotnet build backend/GroceryEasy.sln

# 3. Formatting matches .editorconfig.
dotnet format --verify-no-changes backend/GroceryEasy.sln

# 4. Full test run. Note the --solution flag: on this SDK,
#    `dotnet test <sln>` is rejected outright.
dotnet test --solution backend/GroceryEasy.sln
echo "exit code: $?"     # must be 0
```

Then:

- **No `Co-Authored-By` trailer** in any commit: `git log --format=%B <range> | grep -i "co-authored-by"` must print nothing.
- **Conventional Commits** across the range: `git log --oneline <range>`.
- **No `Version` attribute** in any project file: `grep -rn 'PackageReference.*Version=' backend/**/*.csproj` must print nothing.
- **No `Database.Migrate()`** anywhere: `grep -rn 'Database.Migrate' backend/src` must print nothing.
- **No `DateTime.UtcNow`** in Domain or Application: `grep -rn 'DateTime.UtcNow' backend/src/GroceryEasy.Domain backend/src/GroceryEasy.Application` must print nothing.
- **Every new decision has three artifacts** — an entry in `docs/adr/`, a section in `docs/engineering-decisions.md`, and a `docs/concepts/*.md` primer following `TEMPLATE.md`.

> **Note on the current test-run failure.** Before Phase 1's Task 1.7 lands, check 4 above fails with exit code 8 and the message "Zero tests ran" against `GroceryEasy.ArchitectureTests`, even though 48 tests pass. That is a known state, recorded in [`phase-1-remaining.md`](phase-1-remaining.md), not a regression. It stops being acceptable the moment Task 1.7 is claimed done.

---

## Phase 0 — Foundation · complete

```bash
docker compose --profile core up -d --wait   # exits 0
docker compose --profile core ps             # postgres and redis healthy
git tag -l                                   # lists v1-legacy-node
```

`cd legacy-node && npm install && npm start` boots the old app with zero source edits. `docs/legacy-audit.md` covers 20 defects; `docs/adr/` holds 0000, 0001, 0002, 0013 plus the index.

---

## Phase 1 — .NET skeleton, identity, continuous integration

```bash
dotnet test --solution backend/GroceryEasy.sln    # exit 0, every project reports tests
```

| Gate | How to check |
|---|---|
| Refresh-token reuse detection | The named test passes: replaying a rotated token returns 401 **and** revokes every token sharing its `family_id` |
| Architecture tests | All five pass; the project no longer reports "zero tests ran" |
| Health readiness | `/health/ready` returns 503 with a migration pending, 200 once applied |
| Configuration validation | Remove `Jwt__SigningKey`, start the API, and the process fails at boot with a readable message |
| Error contract | A validation failure returns 400 as RFC 9457 ProblemDetails with a populated `errors` member |
| Continuous integration | Green on the pull request; branch protection on `main` requires it |
| Decision records | ADRs 0003, 0004, 0005, 0006, 0014 accepted and moved out of the Planned table |

---

## Phase 2 — Catalogue, tenancy, images, search

```bash
dotnet run --project backend/src/GroceryEasy.Migrator -- seed demo --stores 3 --reset
dotnet run --project backend/src/GroceryEasy.Migrator -- seed demo --stores 3   # idempotent, no doubling
curl "http://localhost:8080/api/products?q=amool"                               # returns Amul products
```

| Gate | How to check |
|---|---|
| **Tenant isolation** | Store A staff requesting store B's product returns **404**, not 403 |
| Filter coverage | The architecture test asserting a query filter on every `ITenantEntity` passes; removing one filter makes it fail |
| Admin authorization | The `[Theory]` over every admin endpoint returns 403 for a Customer |
| Search safety | `'; DROP TABLE products; --`, `.*` and `{"$gt":""}` as queries return empty results, not errors |
| Index usage | `EXPLAIN ANALYZE` on the full-text query shows the GIN index in use — output captured in the phase report |
| Page size | Requesting 999 results returns at most 50 |
| Images | The database stores object keys; no image bytes anywhere in any table |
| Decision records | ADR-0009 and ADR-0010 accepted |

---

## Phase 3 — Single-page application foundation

```bash
cd frontend/web
pnpm install && pnpm exec tsc --noEmit && pnpm lint && pnpm test && pnpm build
pnpm codegen && git diff --exit-code        # generated client is not stale
```

| Gate | How to check |
|---|---|
| Silent refresh | Log in, wait past the 15-minute access-token lifetime, act, and stay logged in |
| **No refresh stampede** | A test asserts three simultaneous 401 responses produce exactly **one** refresh call |
| Token storage | The access token appears nowhere in `localStorage` or `sessionStorage` — check the browser storage panel |
| Error handling | No component reads `error.response.data.message`; every message goes through the normalizer |
| Mobile | Every screen usable at 360 pixels wide |
| Codegen enforcement | Committing a C# response change without regenerating the client turns continuous integration red |
| Legacy still live | The public URL still serves the old app; `render.yaml` and `Procfile` still present |

---

## Phase 4 — Cart, fulfilment, order placement · the spine

This is the phase the project is judged on. Check it hardest.

| Gate | How to check |
|---|---|
| **Concurrency** | 20 parallel orders against `on_hand = 10` yield exactly 10 successes and 10 `OutOfStock`, `on_hand` ends at 0, and the ledger sums to zero — **run it ten consecutive times**; a flaky pass is a fail |
| **Idempotency** | The same key twice yields one order and two identical bodies; the same key with a different body yields 422 |
| Pricing engine | 100 percent line coverage; `grand_total` equals the sum of the lines in every case, rounding cases included |
| Tax | Extracted from a tax-inclusive price, not added on top; central and state components split evenly |
| State machine | The `[Theory]` covers the **full** legal and illegal matrix for both fulfilment branches |
| Slot capacity | Two concurrent bookings of the last slot yield one success and one `SlotFull` |
| Order ownership | Another user's order returns **404** |
| Outbox | A forced rollback after the outbox insert dispatches nothing |
| No money in the request | `PlaceOrderCommand` has no price, tax or total field — read the type |
| End to end | A pickup order and a delivery order both placed in a real browser with Cash on Delivery |
| Cutover | The public URL serves the new app; `render.yaml` and `Procfile` deleted; `legacy-node/` still present and still frozen |
| Decision record | ADR-0007 accepted |

Also report: the database connection pool size used for the concurrency test.

---

## Phase 5 — Payments, admin console, real-time

| Gate | How to check |
|---|---|
| **Webhook is the source of truth** | With a real test card: the order is still `PendingPayment` after the client callback and becomes `Confirmed` only once the webhook lands. State how the webhook was delivered |
| Signature verification | A tampered webhook signature returns 401 with **no** state change |
| Webhook idempotency | The same provider event identifier delivered twice produces exactly one side effect |
| Reconciliation | An injected mismatch is detected and reported |
| Hangfire | The dashboard lists every recurring job with a next-execution time, and is unreachable without `PlatformAdmin` |
| Interim jobs migrated | The two Phase 4 `IHostedService` timers are gone, along with their `TODO` comments |
| Real-time | Two browser windows: staff marks ready, the customer window updates instantly with the pickup code |
| Pickup codes | No plaintext code exists in the database; hashing, expiry and the five-attempt limit are unit-tested |
| Admin authorization | The endpoint theory covers every newly added admin route |
| Secrets | `git log -p` shows no real key for `Payments__Razorpay__*` |
| Decision records | ADR-0008 and ADR-0012 accepted |

---

## Phase 6 — Hardening, docs, deploy

| Gate | How to check |
|---|---|
| **Clean clone, different machine** | `cp .env.example .env && docker compose --profile full up -d`, application reachable, no manual step. Report what broke on the first attempt |
| Live demonstration | The deployed link works from a phone on mobile data; a cold start reads as loading, not as an error |
| End-to-end tests | All three Playwright specifications green in `e2e.yml`, in the workflow, not just locally |
| Rate limiting | Six rapid login attempts return 429 with a ProblemDetails body |
| Cache invalidation | An admin product edit is visible on the public page immediately, proving tag invalidation rather than expiry |
| Query plans | `EXPLAIN ANALYZE` output in the README; every list endpoint issues one query |
| Tracing | A distributed trace image of `PlaceOrder` in the README |
| Migrations at release | Applied as a separate step; the runtime database user has no schema-modification permission; `/health/ready` fails when a migration is pending |
| README | Hero animation, live link, credentials table, C4 diagram, six linked highlights, the audit table, the decisions table, honest limitations |
| Records complete | `docs/adr/README.md` has **no rows left in the Planned table**; every `docs/concepts/*.md` reconciled against the code |
| Secrets | None in any committed file, image layer or workflow log |
| Legacy intact | `legacy-node/` still present, still frozen, still tagged |

---

## The reporting protocol every phase shares

A phase is reported complete with:

1. **Each gate above, with the command output that proves it** — not a claim that it passes.
2. **Every deviation from the brief, named, with the reason.** Justified deviations are fine; silent ones are not.
3. **Anything found that belongs in `docs/legacy-audit.md` or a later brief.**
4. **The branch name and the commit range.**

If something in a brief turns out to be wrong or impossible, say so and stop, rather than working around it silently. The briefs were written before the code and can be wrong in specifics.
