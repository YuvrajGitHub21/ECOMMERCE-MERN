> **Status note:** this is the original planning document written before Phase 0 began, kept here verbatim as the working record of the plan and its reasoning. It is **not** kept in sync with implementation. For what was actually decided and why, see [`docs/engineering-decisions.md`](../engineering-decisions.md) and [`docs/adr/`](../adr/README.md) — if this document and those disagree, those win. For per-phase execution detail, see [`docs/plans/`](.).

# GroceryEasy → Senior-Grade Portfolio Platform

## Context

GroceryEasy is a MERN grocery e-commerce app built in the 2nd year of college. The product idea is good and still worth building: help local kirana stores get online against Blinkit/Zepto, with **customer-chosen self-pickup or delivery** as the differentiator.

The problem is that the current code does not support that story on a resume claiming 4–6 years of experience. An audit of the repo found:

- **The differentiator was never built.** A repo-wide search for `pickup|self.?pick|fulfillment|deliveryType` returns zero matches. Checkout is delivery-only with a hardcoded `subtotal > 1000 ? 0 : 200` shipping rule duplicated in two files.
- **Order totals are forged-able.** [`orderControllers.js:19-29`](backend/controllers/orderControllers.js#L19-L29) writes client-supplied `itemsPrice/taxPrice/totalPrice` straight to the DB and stamps `paidAt: Date.now()` unconditionally. `POST /order/new` with `totalPrice: 1` is persisted as a paid order.
- **Payment is a `setTimeout`.** [`Payment.js:78-88`](frontend/src/component/Cart/Payment.js#L78-L88) fakes a 2-second delay and hardcodes `{id: "mock_payment_id", status: "succeeded"}`. The Stripe controller exists but its route is commented out and the `stripe` package isn't installed.
- **20 defects total**, including an IDOR on review deletion, an inert admin route guard, an unawaited `forEach` stock decrement whose rejection calls `process.exit(1)`, and a broken password reset. Two order-detail screens are placeholders — one renders the literal string `"OrderDetails Nahi ho raha error solve"`.
- No tests, no CI, no Docker, no linting on the backend, no validation layer, no indexes on any model.

**Decisions made:**

| Decision | Choice |
|---|---|
| Backend | Full rewrite to **ASP.NET Core (.NET 10 LTS)**, Clean Architecture — matches the employer's C# stack |
| Database | **PostgreSQL + EF Core** (migrate off MongoDB) |
| Frontend | **Vite + React + TypeScript + RTK Query + Tailwind + shadcn/ui** (retire CRA) |
| Migration style | **Clean-room rewrite + one-time data migration.** Not a YARP strangler — see below |
| Legacy bugs | **Freeze and document, do not patch** |
| Hosting | **Free tier**, cold starts accepted |
| Budget | ~14 weeks at ~12 h/week ≈ **170 hours** |

**Verified on this machine:** .NET SDK `10.0.204`, Docker `29.4.1`, Node `v20.19.4`. Target `net10.0` — .NET 9 was an STS release and went out of support 2026-05-12.

**Budget honesty:** the full wish-list is ~250–300 hours. This plan is sized to ~170 and pushes the rest to an explicit ordered cut list (§9). Deciding what *not* to build, and being able to say why, is itself a seniority signal.

---

## 1. Why rewrite instead of strangler-fig

The strangler pattern buys exactly one thing: migrating a system carrying live traffic you cannot drop. There are zero users and ~20 seed products whose images are base64 blobs of laptops and cameras. A YARP strangler would cost ~3–4 weeks — a quarter of the budget — on a gateway project, a cross-stack auth bridge (`jsonwebtoken` HS256 ↔ `JwtBearer`), and Mongo↔Postgres dual-write, all of which gets deleted.

**"The app is never broken" is preserved for free by a different seam — the frontend, not a reverse proxy:**

```
legacy-node/          FROZEN at tag v1-legacy-node, still deployable, never edited again
                      (the whole old app — backend + CRA frontend — moved intact)
frontend/web/         new SPA, built greenfield, replaces the public URL only at Phase 4 end
```

Users re-log in once at cutover (there are none). No dual-write. One bounded context: the whole app.

**Optional artifact, Phase 6, one evening:** add a `GroceryEasy.Gateway` YARP project routing `/api/v1/legacy/**` → Node and everything else → .NET, screenshot it, and write `docs/adr/0001-rewrite-vs-strangler.md` as *"evaluated, prototyped, rejected for this context — here is when I would use it instead."* A rejected option with a written rationale reads better than an over-applied pattern.

---

## 2. Legacy bugs: freeze, don't patch

Do **not** fix the 20 defects in Express. That is 2–3 weeks hardening code deleted in month 2.

**Deliverable (Phase 0): `docs/legacy-audit.md`** — each defect with severity, repro, and a link to the design element that makes the *class* of bug unrepresentable. Condensed table goes in the README. This is the highest-leverage artifact in the project:

| # | Legacy defect | Structural prevention |
|---|---|---|
| 1 | Client-supplied prices; unconditional `paidAt` | `PlaceOrderCommand` carries **no money fields at all** — only `cartId`, `fulfillmentType`, `slotId`, `addressId`. `paidAt` set only by the webhook handler. |
| 2 | `setTimeout` fake payment | Razorpay order created server-side for the server-computed amount; webhook is source of truth |
| 3 | `DELETE /reviews` has no ownership check (IDOR) | Resource-based `IAuthorizationHandler`; `unique (product_id, user_id)`; `order_id` FK requires verified purchase |
| 4 | `isAdmin={true}` on the inner `<Route>`, so all 9 admin pages render for any user | Guard is server-side (`RequireAuthorization("StoreStaff")`); a `[Theory]` asserts 403 for a Customer on **every** admin endpoint |
| 5 | Deleted user's token 500s the app | JWT validation with `SecurityStamp`; stale user → 401 by construction |
| 6 | Unawaited `forEach` stock decrement; one rejection calls `process.exit(1)` | Reservation engine (§5.2); all-or-nothing in one transaction |
| 7 | Stock never validated at order time | Reservation taken at checkout start, before payment; ordering without a live reservation is impossible |
| 8 | Pre-save hook missing `return`, re-hashes password every save | ASP.NET Core Identity owns hashing |
| 9 | Reset link built from attacker-controllable `req.get("host")` | URL from validated `Frontend:BaseUrl` config; `Host` header never used to build URLs |
| 10 | `OrderDetails` is an 11-line stub | Real screen + Playwright E2E |
| 11 | Cart never cleared; double-submit → duplicate orders | Idempotency keys + `Cart.Clear()` in the same transaction |
| 12 | `error.response.data.message` → every toast shows `undefined` | Single RFC 9457 ProblemDetails contract + one error normalizer + generated TS types |
| 13 | `GET /order/:id` is admin-only; users can't see own orders | `OrderOwnerOrStoreStaff` policy |
| 14 | Raw user input into `$regex` (ReDoS) + NoSQL operator injection | Parameterized SQL via EF; `EF.Functions.WebSearchToTsQuery` (safe parser) |
| 15 | String rating → `("0"+"5"+4)/2` = 27-star average | `rating smallint CHECK (1..5)` + `Rating` value object + SQL `AVG` |
| 16 | `resultPerPage=999`; `productCount` vs `productsCount`; no `.sort()` | Keyset pagination, `MaxPageSize=50` server-enforced, one typed `PagedResult<T>` shared via codegen |
| 17 | No indexes; `pinCode`/`phoneNo` as Number; no enums; no `updatedAt` | `IEntityTypeConfiguration<>` per entity; CHECK constraints; audit interceptor |
| 18–20 | Dual MUI v4+v5; LAN proxy `192.168.179.1`; laptop/camera categories | Greenfield SPA; `VITE_API_BASE_URL`; categories from the `categories` table |

---

## 3. Repo layout

```
GroceryEasy.sln
backend/src/
  GroceryEasy.Domain/          net10.0, ZERO package refs (enforced by arch test)
    Common/      Entity, AggregateRoot, ValueObject, IDomainEvent, Error, Result<T>
    Catalog/     Product, ProductVariant, Category, Unit, Money, StockQuantity
    Inventory/   Inventory, InventoryReservation, StockLedgerEntry
    Ordering/    Order, OrderItem, OrderStatus, OrderStateMachine, PickupCode,
                 FulfillmentType, OrderPricingEngine, GstCalculator
    Stores/      Store, StoreHours, FulfillmentSlot, ServiceablePincode
  GroceryEasy.Application/     → Domain only
    Abstractions/  ICommand<T>, IHandler<,>, IDispatcher, IApplicationDbContext,
                   ICurrentUser, ICurrentStore, IObjectStorage, IPaymentGateway
    Behaviors/     Validation, Logging, Transaction, Idempotency
    Features/      ← VERTICAL SLICES: Orders/PlaceOrder/{Command,Handler,Validator,Response}
  GroceryEasy.Infrastructure/  → Application (EF Core, Identity, Razorpay, R2, Hangfire)
  GroceryEasy.Api/             → minimal APIs, IEndpoint convention, SignalR OrderHub
  GroceryEasy.Migrator/        Mongo→PG + grocery seeder
backend/tests/                 Domain.UnitTests, IntegrationTests, ArchitectureTests
frontend/web/                  Vite + React + TS  (new)
legacy-node/                   FROZEN — the entire old app moved as ONE unit:
  backend/ frontend/ package.json package-lock.json Procfile seed-images/
  (moved intact because app.js resolves ../frontend/build and loads
   backend/config/config.env relative to CWD — splitting them breaks both)
docs/ adr/ diagrams/ legacy-audit.md
deploy/  Dockerfile.api  Dockerfile.web
.github/workflows/   docker-compose.yml
```

### Opinionated architecture calls

1. **Vertical slices *inside* the four layers.** Keep the project boundary (that's what "Clean Architecture" means to an interviewer, and NetArchTest enforces it), but organize `Application` by feature folder, not technical type. A folder of 40 `IOrderService.cs` interfaces is the junior smell; co-located slices are the senior one.

2. **No MediatR — hand-roll a ~150-line dispatcher.** MediatR moved to a commercial license (v13, Sept 2025). Define `ICommand<T>`, `ICommandHandler<,>`, `IDispatcher`; register by assembly scan via **Scrutor**; behaviors are a `Scrutor.Decorate<>` chain. Fully debuggable, and implementing the pattern reads better than importing it. `ADR-0004` records the licensing rationale.

3. **No AutoMapper.** Same vendor, same license change, and it moves mapping bugs to runtime. Write `static ProductResponse ToResponse(this Product p)` next to each slice. For queries, **project inside the EF query** (`.Select(p => new ProductListItem {...})`) so Postgres returns only the columns used — which structurally kills bug #16's "returns the whole catalog including base64 blobs".

4. **`Result<T>` for expected failures, exceptions for bugs.** `OutOfStock`, `SlotFull`, `PincodeNotServiceable`, `CartEmpty`, `StoreClosed` → `Result`. Null args, invariant violations, DB down → `throw`. One `Result → IResult` extension maps to **RFC 9457 ProblemDetails** with a stable `type` URI, permanently killing bug #12.

5. **No generic repository.** `DbContext` *is* the Unit of Work; expose `IApplicationDbContext` (just `DbSet<>`s + `SaveChangesAsync`). Add narrow aggregate repositories only where there's real loading logic: `IOrderRepository.GetForStatusTransitionAsync`, `IInventoryRepository.TryReserveAsync`. Three repositories, not fifteen. `IRepository<T>` over EF is the loudest "I read one blog post" signal in .NET.

6. **Minimal APIs** with `MapGroup` + an `IEndpoint` convention, `TypedResults` everywhere so response types flow into the OpenAPI doc.

7. **FluentValidation** in a `ValidationBehavior` before the handler, so handlers never re-check inputs. Domain invariants live in aggregate methods and throw. Two layers, clearly separated.

8. **Inject `TimeProvider`** (BCL). Never `DateTime.UtcNow` in Domain/Application — slot cutoffs, reservation expiry, and pickup-code validity become deterministically testable.

---

## 4. Data model (PostgreSQL 17)

Conventions: `snake_case` (Npgsql `UseSnakeCaseNamingConvention`), **UUIDv7** PKs (`Guid.CreateVersion7()` — sequential, dense B-trees, no enumeration leak), `timestamptz`/`DateTimeOffset`, money `numeric(12,2)`, quantity `numeric(10,3)` (loose goods sold in 250 g steps), `created_at`/`updated_at` via a `SaveChanges` interceptor, `xmin` as concurrency token on mutable aggregates.

**Stores** — `stores` (slug, timezone `Asia/Kolkata`, gstin), `store_hours`, `store_closures`, `store_staff` (the multi-tenant authorization join).

**Identity** — ASP.NET Core Identity tables (`IdentityUser<Guid>` subclass + `full_name`, `phone_e164`, `default_store_id`, `legacy_mongo_id`), plus:
- `refresh_tokens` — `token_hash` (uq), `family_id`, `replaced_by_id`, `revoked_at` → rotation + reuse detection
- `addresses` — **`pincode varchar(6)`**, **`phone varchar(15)`** (never Number — bug #17)
- `otp_challenges` — phone, `code_hash`, attempts, TTL

**Catalog** — `categories` (store-scoped or platform-global), `products` (`gst_rate`, `hsn_code`, `search_vector tsvector GENERATED ALWAYS AS (...) STORED` + GIN, `legacy_mongo_id` uq), **`product_variants`** (`sku`, `unit` enum Piece/Gram/Kilogram/Millilitre/Litre, `pack_size`, `mrp`, `sale_price`, `is_loose`, `step_qty`, `barcode`, `reorder_level`), `product_images` (**`object_key`, never bytes**), `inventory` (`on_hand`, `reserved`, `xmin`; available = on_hand − reserved), `inventory_batches` (FEFO), **`stock_ledger`** (append-only: delta, reason, ref — never mutate stock without a ledger row), `reviews` (`rating smallint CHECK (1..5)`, `order_id` for verified purchase, `unique (product_id, user_id)`).

> `product_variants` is what makes this a *grocery* app, not a clone: Aashirvaad Atta 1 kg / 5 kg / 10 kg are three variants.

**Cart & fulfillment** — `carts` + `cart_items` (**server-side, not localStorage**), `serviceable_pincodes` (fee, min order, free-above threshold, ETA), `fulfillment_slots` (`slot_date`, `type` Pickup/Delivery, `capacity`, `booked_count`), `inventory_reservations` (`expires_at`, status Held/Committed/Released).

**Orders** — `orders` (`order_number` `GE-2026-000123` from a sequence, **`fulfillment_type`**, `slot_id`, `pickup_code_hash`, the full price breakdown, `pricing_version`, `xmin`), `order_items` (**`*_snapshot` columns** so a later price edit never rewrites history), `order_status_history`, `payments`, `payment_events` (`provider_event_id` uq → webhook idempotency), `refunds`.

**Infrastructure** — `idempotency_keys`, `outbox_messages`, `import_jobs`/`import_rows`, `hangfire.*`.

### Images: base64 → object storage

**Cloudflare R2** (S3-compatible, **zero egress**, 10 GB free) via `AWSSDK.S3` with a custom `ServiceURL`; **MinIO** in compose locally — identical API, same code path in dev and prod.

Migration step: parse the data URI → sniff content type from magic bytes (don't trust the declared MIME) → **SHA-256 the bytes → key `products/{sha256}.{ext}`**, which is what makes blob migration idempotent → downscale with `SixLabors.ImageSharp` to 1200/600/200 px WebP → insert `product_images` row with the key. Admin uploads go direct to R2 via **presigned PUT**; image bytes never touch the API, so the 13 MB `express.json` limit disappears.

### `GroceryEasy.Migrator`

`Microsoft.Extensions.Hosting` console app + `MongoDB.Driver`, Spectre.Console progress:

```
migrate users|products|orders|images|all  --dry-run --batch-size 200
seed demo --stores 3 --reset
```

**Idempotency:** every migrated row carries `legacy_mongo_id` with a unique partial index; batches are `INSERT ... ON CONFLICT (legacy_mongo_id) DO UPDATE` in a transaction. A `migration_runs` table records the last processed `_id` so an interrupted run resumes. `--dry-run` runs inside a transaction and rolls back.

**Data fixes applied during migration** (each closes an audit finding): `pinCode`/`phoneNo` Number → varchar; free-string `role` → validated enum; string `rating` (bug #15) → `int.TryParse` + clamp 1–5, unparseable rows written to `migration_rejects` rather than silently dropped.

> **Build the migrator, but seed the demo separately.** The existing catalog is wrong-domain data (Laptop, Footwear, Camera). Migrating it gives you a grocery app full of cameras. The migrator is a portfolio artifact demoed against a dump of the old DB; `seed demo` populates 3 stores and ~120 real Indian grocery SKUs across 8 categories.

Schema is **EF Core migrations only**. The Migrator consumes the EF model; it never defines schema.

---

## 5. Correctness engineering

### 5.1 Order pricing engine

`Domain/Ordering/OrderPricingEngine.cs` — a **pure function**, no I/O, no DI:

```
Price(IReadOnlyList<PricedLine> lines, FulfillmentChoice choice, StoreConfig cfg) -> OrderPricing
```

- `PricedLine` is built by the handler from the **database** (`variant.sale_price`, `product.gst_rate`), never from the request.
- **GST is India-correct**: Indian retail prices are GST-*inclusive*, so tax is **extracted**: `tax = line_total − (line_total / (1 + rate))`, split CGST/SGST 50/50 intra-state. Getting this right is a domain-credibility signal.
- Loose goods: `qty` validated as a multiple of `step_qty`.
- Delivery fee: `0` for Pickup; else `serviceable_pincodes.delivery_fee`, waived above `free_delivery_threshold`.
- **Pickup discount** (default 2%, capped) — the differentiator, made visible in money.
- **Rounding decided once**: banker's rounding is wrong for currency → `MidpointRounding.AwayFromZero` at 2 dp, with the grand total reconciled to the sum of lines so it can't drift by a paisa. A dedicated rounding test suite is a strong signal.
- Store `pricing_version` on every order so an engine change never retroactively alters old invoices.

### 5.2 Inventory reservation — the concurrency design

**Chosen: reservation table with TTL + atomic conditional UPDATE, plus `xmin` optimistic concurrency for admin edits.** Three mechanisms, each for a different job.

- **Rejected `SELECT ... FOR UPDATE`:** a multi-item order locks N rows; two concurrent orders with overlapping items in different order **deadlock**. Sorting by `variant_id` fixes it but makes correctness depend on every future caller remembering.
- **Rejected pure `xmin` on inventory:** correct, but under contention on a hot SKU it retries and can livelock, re-running the whole handler each time. Kept for *admin* edits (low contention, where "someone else changed this" is the right UX).
- **Chosen:**
  ```sql
  UPDATE inventory SET reserved = reserved + @qty
   WHERE variant_id = @id AND on_hand - reserved >= @qty
  ```
  Postgres serializes writers on the row internally; `0 rows affected` **is** the out-of-stock answer, with no read-then-write window. No explicit locks, no retry loop, no deadlock — *provided items are applied in `variant_id` order*, enforced in one place (`InventoryRepository.TryReserveAsync`) with a test.

**Flow:** `POST /checkout/start` reserves every line + books the slot **in one transaction**, writes ledger rows, sets `expires_at = now + 15 min` → `POST /orders` creates the order in `PendingPayment` → webhook `captured` commits reservations (`on_hand -= qty`, `reserved -= qty`) and moves the order to `Confirmed` → a Hangfire job releases expired holds every 60 s.

> **The test that sells this in an interview:** 20 concurrent `PlaceOrder` calls against a SKU with `on_hand = 10` → **exactly 10 succeed, 10 return `OutOfStock`, `on_hand` ends at 0, ledger sums to zero drift.** Name this test in the README.

### 5.3 Idempotency keys

`IdempotencyBehavior` on commands marked `IIdempotentCommand`. Client sends `Idempotency-Key: <uuidv7>`. `INSERT INTO idempotency_keys (...)` — **a unique violation is how you detect the duplicate without a lock**. Completed → replay the stored response verbatim; InFlight → `409` + `Retry-After`; same key + different body hash → `422`.

The frontend generates the key **once when the checkout screen mounts**, not per click, so a double-click, a retry, and a flaky-network resend all collapse to one order. That is the structural fix for bug #11.

### 5.4 Transactional outbox — justified

Real, lossy side effects exist (order confirmation emails, SignalR broadcasts, low-stock alerts) and the failure it prevents is demoable: *order committed, process crashes, customer never learns their order exists*.

A `SaveChangesInterceptor` collects domain events off tracked aggregates into `outbox_messages` **in the same transaction** as the state change. A Hangfire job (5 s) claims a batch with **`FOR UPDATE SKIP LOCKED`** (the correct pattern, worth knowing), dispatches, applies exponential backoff, dead-letters after 5 attempts. ~150 lines. ADR notes that `LISTEN/NOTIFY` or a broker is the scale answer.

### 5.5 Order state machine

`Order` has **private setters**; the only mutation path is `order.TransitionTo(status, actor, reason)`. Legal transitions live in a `static readonly FrozenSet<(OrderStatus, OrderStatus)>`, **branching on fulfillment type**:

```
Common:   Draft → PendingPayment → Confirmed
          PendingPayment → Expired | PaymentFailed
          Confirmed → Cancelled   (only while not yet Packed; raises a refund event)

Pickup:   Confirmed → Packing → ReadyForPickup → PickedUp → Completed
                                ReadyForPickup → NoShow   (job, slot end + 4 h)

Delivery: Confirmed → Packing → OutForDelivery → Delivered → Completed
                                OutForDelivery → DeliveryFailed → OutForDelivery
```

Every transition writes `order_status_history` (who/when/why) and raises a domain event → outbox → email + SignalR. Illegal transitions return `Result.Failure(OrderErrors.InvalidTransition(from, to))` — never an exception, never a silent no-op. Unit tested with a `[Theory]` over the full legal/illegal matrix.

---

## 6. Features

### 6.1 Pickup vs delivery — build it fully, first, demo it first

- **Slot generation:** a nightly job materializes 7 days of `fulfillment_slots` per store from `store_hours` minus `store_closures` — 60-min Pickup slots (capacity 8), 90-min Delivery slots (capacity 4). Materializing rows (vs computing on the fly) is what makes capacity booking atomic.
- **Booking** uses the same conditional-UPDATE trick: `SET booked_count = booked_count + 1 WHERE id = @id AND booked_count < capacity`. Zero rows → `SlotFull`. Slot booking and inventory reservation share **one transaction** — you can never hold stock without a slot or vice versa.
- **Cutoffs:** slot hidden once `now > slot_start − lead_time` (30 min pickup, 60 min delivery), computed in `Asia/Kolkata` via `TimeZoneInfo` + `TimeProvider`. Timezone-correct slot logic is a detail interviewers poke at.
- **Pickup code:** on `ReadyForPickup`, generate 6 digits, store **only** an HMAC-SHA256 hash + 24 h expiry, push plaintext to the customer once via SignalR + email. Staff enters it → constant-time compare → `PickedUp`. Rate-limited to 5 attempts/order.
- **Serviceability:** `GET /stores/{id}/serviceability?pincode=411001`, cached 1 h. **The cart page checks it before the customer reaches checkout**, not after — that's the UX difference between a product and a tutorial.
- **Differential pricing, rendered as a live comparison on the cart screen** — this single screenshot *is* the product vision:
  ```
  Pickup    ₹0 delivery    −2% pickup discount   Ready in 45 min     → ₹487.20
  Delivery  ₹29 delivery   free above ₹499       Arrives 6–7:30 pm   → ₹526.00
  ```

### 6.2 Payments — Razorpay + COD

**Razorpay over Stripe:** Stripe India requires a registered business entity to onboard, so you likely can't get a working demo; Razorpay issues test keys to an individual immediately, and the whole domain (₹, GST, pincode, kirana) is Indian. **Include Cash on Delivery** — a kirana app without COD is not credible, and it's ~30 lines of domain judgment.

**Webhook is the source of truth; the client is never trusted:**
1. `POST /orders` → `PendingPayment`; server calls Razorpay Orders API with `amount = grand_total × 100` **from the pricing engine**. Bug #1 is unreachable.
2. Client handler posts the signature triple → server verifies `HMAC_SHA256(order_id + "|" + payment_id, key_secret)` constant-time. This only **unblocks the UI**; it does not confirm the order.
3. `POST /webhooks/razorpay` confirms it: verify `X-Razorpay-Signature` over the **raw request body** (`EnableBuffering()` — deserializing and re-serializing breaks the signature; this is the #1 webhook bug, worth a code comment), insert into `payment_events` keyed on provider event id (unique violation = already processed → 200 immediately), then handle `payment.captured` / `payment.failed` / `refund.processed`.
4. **Refunds** on admin cancellation; **daily reconciliation job** diffs Razorpay's payment list against `payments` and alerts on any order marked paid with no provider record. ~60 lines, and very few portfolio projects can say "I built payment reconciliation."

`FakePaymentGateway` behind `IPaymentGateway` for dev/test; integration tests never touch Razorpay.

### 6.3 Catalog & inventory

- **Variants/units — mandatory.** Loose goods (`is_loose`) sold in `step_qty` increments at a per-kg price; the PDP stepper renders "250 g / 500 g / 1 kg" instead of "1 / 2 / 3".
- **Low-stock alerts:** `reorder_level` per variant → daily 08:00 IST digest + a SignalR badge in the admin console.
- **Bulk CSV import:** `CsvHelper`, two-phase — upload → parse into `import_rows` → **validate & preview with per-row errors** → commit, with a downloadable error CSV. The most "real internal tooling" feature in the list, ~250 lines, and it demos beautifully.
- **Batch/expiry FEFO** — on the cut list (§9).

### 6.4 Search — Postgres, not Elasticsearch

The catalog is 100–2,000 SKUs. Elasticsearch adds a container, a memory floor, an index-sync pipeline, and a consistency problem for a corpus Postgres answers in <5 ms. **"I added Elasticsearch to search 500 rows" reads as poor judgment, not experience.** The written ADR *is* the signal.

Generated `search_vector` weighting name (A) > brand (B) > category (C) > description (D); GIN index; query via `EF.Functions.WebSearchToTsQuery` (safe parser — bug #14 gone); rank with `ts_rank_cd`; **`pg_trgm` similarity fallback** when FTS returns <3 hits so "amool" finds "Amul" and "magi" finds "Maggi". Faceted filters as indexed predicates, keyset pagination, 150 ms-debounced autocomplete capped at 8.

> Put the `EXPLAIN ANALYZE` output for the FTS query in the README showing the index is used. Almost nobody does this.

### 6.5 Auth

**Use ASP.NET Core Identity** (EF stores, `IdentityUser<Guid>`) — vetted hasher, lockout, security stamps, token providers, and every .NET interviewer knows it. Rolling your own spends a week to arrive somewhere worse. Build the **token layer** yourself, which is where the signal is:

- **Access token:** JWT, 15 min, **in memory in Redux only** — never `localStorage` (XSS), never a cookie the API reads (CSRF).
- **Refresh token:** opaque 256-bit, stored **hashed**, `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`.
- **Rotation with reuse detection:** each refresh issues a new token and marks the old `replaced_by`. Presenting an already-rotated token means it leaked → **revoke the whole `family_id`**, log a security event, force re-login. ~80 lines, and it demonstrates you understand *why* rotation exists.
- **Roles + policies:** `Customer`, `StoreStaff`, `StoreManager`, `PlatformAdmin`. Policies do the real work — `RequireStoreAccess`, and resource-based `OrderOwnerOrStoreStaff` / `ReviewAuthorOrStoreStaff`, closing bugs #3, #4, #13 with authorization primitives rather than `if` statements.
- **Email verification** → link to the **SPA** route from `Frontend:BaseUrl`, never `Request.Host` (bug #9). Unverified users browse but can't order.
- Built-in `RateLimiter` (5/min) on login/OTP/forgot-password; `DataProtection` keys persisted to Postgres so tokens survive a container restart.
- **OTP phone login** — cut-list item.

### 6.6 Observability

- **Serilog** → compact JSON in prod, **Seq** locally. Enrichers: correlation id, user id, **store id**, path. `UseSerilogRequestLogging` — one structured line per request.
- **OpenTelemetry** (ASP.NET Core + HttpClient + **Npgsql** + EF Core) → OTLP → the **.NET Aspire Dashboard** container. Zero config, free, and a distributed trace of `PlaceOrder` showing the transaction, the Razorpay call, and the outbox insert is a fantastic README image.
- **Custom business metrics** via `System.Diagnostics.Metrics`: `groceryeasy.orders.placed` **tagged by fulfillment type** (so the differentiator is measurable), `checkout.duration`, `payment.failures`, `reservations.expired`. Business metrics, not just CPU, is the maturity signal.
- **Health checks:** `/health/live` (process) and `/health/ready` (Npgsql + Redis + R2 + **pending migrations**). Directly replaces the `GET /api/v1` → `"Hello World"` probe that never touched the DB.

### 6.7 Background jobs — Hangfire

`Hangfire.PostgreSql`. Over a bare `IHostedService`: jobs survive restarts, retry with backoff, and **the dashboard is a screenshot** (mounted at `/hangfire` behind `PlatformAdmin`).

| Job | Cadence |
|---|---|
| `OutboxDispatcherJob` | 5 s |
| `ReleaseExpiredReservationsJob` | 1 min |
| `GenerateFulfillmentSlotsJob` | daily 00:30 IST, 7 days ahead |
| `LowStockDigestJob` | daily 08:00 IST |
| `PaymentReconciliationJob` | daily 02:00 IST |
| `MarkNoShowPickupsJob` / `AbandonedCartNudgeJob` / `PurgeIdempotencyKeysJob` | hourly / hourly / daily |
| `ResetDemoDataJob` | daily 03:00 IST (demo env) |

### 6.8 Real-time — SignalR

One `OrderHub`, groups `order:{orderId}` (customer) and `store:{storeId}` (staff). Auth via the standard `OnMessageReceived` handler reading `access_token` from the query string (WebSockets can't set headers). **Fired from outbox handlers**, so a notification is never sent for a rolled-back transaction.

**Demo moment:** two windows side by side — staff clicks "Ready for Pickup", the customer's screen updates instantly and shows the pickup code. That's the README GIF.

No backplane (single instance). State in the README that Redis backplane is the scale-out answer — knowing you *don't* need it yet is the senior read.

### 6.9 Multi-tenancy — yes, "lite" (single DB, `store_id` discriminator)

Build it **from day one in Phase 2**, in the cheapest form. It *is* the product vision ("help kirana stores" is plural or it's nothing), it costs ~5–6 evenings designed in versus a month retrofitted, and tenant isolation is dead-center senior interview territory. It converts "another e-commerce clone" into "a B2B2C platform".

- `store_id` on every tenant-owned table + an `ITenantEntity` marker.
- `ICurrentStore` resolved by middleware from route slug `/s/{storeSlug}/...` → staff `store_id` claim → user's `default_store_id`.
- **EF Core global query filters** applied by convention in `OnModelCreating` to every `ITenantEntity`, so a developer who forgets a `.Where(...)` still cannot read another store's data.
- A `SaveChanges` interceptor **stamps `store_id` on insert** and **throws on any attempt to modify it**.
- **The proof:** an integration test where store A's manager requests store B's order and gets **404, not 403** (don't leak existence), plus an arch test asserting every `ITenantEntity` has a configured filter.

**Explicitly out of scope:** self-serve onboarding, subdomains, per-store branding, billing, schema-per-tenant. Three seeded stores and one hand-written "create store" endpoint make the claim true.

### 6.10 Caching — `HybridCache` over Redis

`Microsoft.Extensions.Caching.Hybrid` (shipped in .NET 9): L1 in-process + L2 Redis, **built-in stampede protection**, tag-based invalidation. Cache the category tree (1 h), product list pages (60 s), product detail (5 min), pincode serviceability (1 h) — all invalidated by tag on admin write. Slot availability gets a short TTL only; booking races make invalidation the wrong tool there.

**The cart is NOT cached in Redis as source of truth** — `carts`/`cart_items` in Postgres is the truth (that's the fix for localStorage carts); Redis holds only the derived header-badge summary. Say this in the ADR; "I used Redis for the cart" is a common junior answer with a data-loss failure mode.

---

## 7. Testing

**No global coverage target** — chasing repo-wide 80% burns 30 hours testing DTO mappers. Targets by layer:

| Layer | Target |
|---|---|
| `Domain` | **≥85% line, 100% of the pricing engine and state machine** — xUnit v3 + **Shouldly**, zero mocks |
| `Application` | every command has ≥1 happy + ≥1 failure test, driven **through the API** |
| `Api` | authorization asserted on every protected endpoint |
| `web` | ~40% — cart math, pricing display, auth flow, error normalizer (Vitest + RTL + MSW) |

> **Shouldly, not FluentAssertions** — FluentAssertions v8 moved to a commercial license in 2025. Same class of decision as MediatR and AutoMapper; cover all three in one ADR on dependency governance.

**Domain unit tests (highest density here):** GST extraction + CGST/SGST split; the ₹0.005 rounding cases; the free-delivery boundary at exactly ₹499; pickup discount cap; loose-quantity step validation; `grand_total == sum(lines)` always; the full state-machine transition matrix; slot cutoffs across the IST boundary with a fake `TimeProvider`; pickup-code hashing/expiry/attempt limits.

**Integration tests** — `WebApplicationFactory<Program>` + `Testcontainers.PostgreSql` + **Respawn** in an `IAsyncLifetime` collection fixture (one container per run, ~20 s startup then fast):
- register → verify → login → refresh → **rotate → replay old token → whole family revoked**
- authorization sweep: `[Theory]` over every admin endpoint asserting 403 for a Customer (permanent regression test for bug #4)
- **tenant isolation:** store A staff → store B's order → 404
- place order: happy (pickup + delivery), out-of-stock, slot-full, non-serviceable pincode, empty cart, closed store
- **idempotency:** same key twice → one order, identical body; same key + different body → 422
- **concurrency:** 20 parallel orders vs stock 10 → exactly 10 succeed, ledger sums to zero
- webhook: valid → confirmed; tampered → 401 with no state change; duplicate event id → 200 with a single side effect
- search injection payloads (`'; DROP`, `.*`, `{$gt:""}`) → empty results, not errors (regression test for bug #14)

**Architecture tests** (NetArchTest, ~50 lines, high signal): Domain references nothing outside the BCL; Application doesn't reference EF Core or Infrastructure; entities have no public setters; handlers are `sealed internal`; every `ITenantEntity` has a query filter.

**E2E (Playwright) — exactly three specs**, more will rot: (1) customer pickup incl. the pickup-vs-delivery comparison and a loose item; (2) delivery + Razorpay test card + simulated webhook; (3) staff processes an order through to pickup-code verification, asserting the customer window updated over SignalR. Run **nightly and on `main`**, not on every PR — flaky E2E on PRs kills CI trust.

---

## 8. DevOps

**Containers.** `deploy/Dockerfile.api`: multi-stage `sdk:10.0` → `aspnet:10.0-noble-chiseled`, non-root (`USER $APP_UID`), no shell in the final image, restore layered for cache, target <120 MB. `deploy/Dockerfile.web`: `node:22-alpine` build → `nginx:1.27-alpine` with SPA fallback, brotli, immutable hashed-asset caching, `no-store` on `index.html`, and runtime env injection via a generated `env.js` so one image works everywhere.

**`docker compose up` must work from a clean clone, first try.** This is the single most-checked thing by anyone evaluating the repo — test it on a different machine; it will be broken the first time.

```
api · web · postgres:17-alpine (healthcheck, pg_trgm init) · redis:7-alpine
mailpit (axllent/mailpit — NOT MailHog, unmaintained since 2020)
minio (S3-compatible local storage) · aspire-dashboard (OTel UI) · seq (logs, optional profile)
```
`depends_on: condition: service_healthy`; profiles `core` and `full`; `.env.example` committed.

**GitHub Actions.** `ci.yml` on every PR: `dotnet format --verify-no-changes`, `build -warnaserror`, unit tests, **integration tests with Testcontainers** (works on `ubuntu-latest` out of the box), then web `tsc --noEmit` + ESLint + Vitest + `vite build`, with NuGet/pnpm caching. Plus `codeql.yml` (weekly), `e2e.yml` (nightly + main), `docker-publish.yml` → GHCR.

**Branch protection on `main` + PR-only workflow, even solo.** A history of reviewed PRs with green checks and Conventional Commits is free evidence of professional habits.

**Migrations at release, not startup.** Never call `Database.Migrate()` in `Program.cs` — it races across instances and gives no rollback point. Instead: CI runs `dotnet ef migrations script --idempotent` and uploads the SQL as a reviewable artifact; deploy runs it as a **separate step before** the new image rolls; the runtime DB user has **no DDL permission**; `/health/ready` fails if `GetPendingMigrationsAsync()` is non-empty, so a mismatched deploy fails its probe instead of half-working. Migrations must be **expand/contract** (add nullable → backfill → make non-null later), never a destructive rename in one step.

### Hosting — free tier

Retire `render.yaml` and `Procfile`. Free stack, chosen to keep cold starts off the parts a recruiter touches first:

| Piece | Where | Note |
|---|---|---|
| SPA | **Cloudflare Pages** | free, global CDN, **no cold start** — the link always feels instant |
| Images | **Cloudflare R2** | 10 GB free, zero egress |
| Postgres | **Neon** free tier | avoids Render Postgres's 90-day expiry, which would silently kill the demo |
| Redis | **Upstash** free tier | |
| API | **Render** free Docker web service | this is the one cold start; keep the container warm with a GitHub Actions cron pinging `/health/live` every 10 min (750 free instance-hours/month covers it) |

Mitigate what remains: the SPA renders its shell and skeletons from the CDN immediately and shows a "waking the demo server…" state on the first API call, so a cold start reads as a loading state rather than a broken link. Put the demo credentials table above the fold so a reviewer knows what to click while it wakes.

> Note in the README that Azure Container Apps + Bicep is the intended production target. **If a `deploy/bicep/main.bicep` + OIDC deploy workflow ever fits in the budget, add it** — reviewable IaC costs one evening and is disproportionately convincing to a .NET hiring manager, even unrunning.

---

## 9. Sequencing

Assume ~12 h/week.

### Phase 0 — Foundation · Week 1 (12 h)
Freeze the legacy app into `legacy-node/` and tag it; write `docs/legacy-audit.md`; ADR-0000/0001/0002/0013; `docker-compose.yml`; repo conventions.
**Done:** `docker compose up` brings up healthy infra; `legacy-node` still runs.
→ **Full execution brief in §12.**

### Phase 1 — .NET skeleton, identity, CI · Weeks 2–4 (36 h) **[blocks everything]**
Four projects + four test projects. EF Core + Npgsql (snake_case, UUIDv7, audit interceptor, first migration). Identity + JWT + **refresh rotation with reuse detection** + email verification. `IEndpoint` convention, `Result<T>` → ProblemDetails, global `IExceptionHandler`, correlation-id middleware. Hand-rolled dispatcher + Scrutor + behaviors. Serilog, health checks, OpenAPI + **Scalar**. Test harness (Testcontainers + Respawn + `TestWebAppFactory`) + **NetArchTest suite**; `ci.yml` green.
**Done:** register → verify → login → refresh → rotate-detection all pass as integration tests in CI on a fresh container.

### Phase 2 — Catalog, tenancy, images, search, migrator · Weeks 4–6 (36 h)
Domain: Store, Category, Product, ProductVariant (units/pack size/loose), Inventory, StockLedger. **Multi-tenancy: `ICurrentStore`, global query filters, stamping interceptor, isolation test — do this now, retrofitting is a month.** R2/MinIO `IObjectStorage` + presigned uploads + ImageSharp renditions. Postgres FTS + `pg_trgm` + facets + keyset pagination. Admin catalog CRUD behind `StoreStaff`. `GroceryEasy.Migrator` + grocery seeder.
**Done:** `migrator seed demo` yields a browsable, searchable, correctly-priced grocery catalog; tenant isolation test green.

### Phase 3 — SPA foundation & catalog parity · Weeks 6–8 (36 h)
`frontend/web`: Vite + React 19 + TS strict, Tailwind v4, shadcn/ui, RTK Query, React Router, react-hook-form + zod. **OpenAPI → RTK Query codegen wired into CI** with a staleness check. Auth slice: in-memory access token, silent refresh on 401 via `baseQueryWithReauth`, route guards. Screens: home, category browse, search + facets, PDP (variant/unit picker, loose-goods stepper), auth, profile, addresses. ProblemDetails → toast normalizer. Vitest on cart math + normalizer.
**Done:** new SPA reaches catalog + auth parity at a `next.` URL; legacy still up.

### Phase 4 — Cart, fulfillment, order placement · Weeks 8–10 (36 h) **[the spine — highest value]**
Server-side cart + anonymous→user merge on login. **Pickup vs delivery**: store hours, slot generation job, capacity booking, pincode serviceability, differential pricing, the cart comparison UI. **Pricing engine** (TDD — it's pure). **Inventory reservation** + release job. **Idempotency behavior**, **outbox**, **order state machine** + status history. `POST /checkout/start`, `POST /orders`, `GET /orders`, `GET /orders/{id}` with ownership policy. Checkout UI: slot picker, address selection, summary, order detail with timeline. All the integration tests in §7.
**Done:** real pickup and delivery orders placed end to end with COD; concurrency test green; **cut the public URL over to the new SPA and delete `render.yaml`/`Procfile`.**

> Nothing in Phase 4 parallelizes: pricing → reservation → order → idempotency/outbox is strictly sequential.

### Phase 5 — Payments, admin console, real-time · Weeks 10–12 (36 h)
Razorpay end to end (order creation, checkout, client verify, **raw-body webhook**, `payment_events` idempotency, refunds, reconciliation) + `FakePaymentGateway`. Admin console on shadcn data tables: orders Kanban by status, transitions with reason capture, **pickup-code verification**, product CRUD with image upload, inventory adjustments, users, reviews. SignalR `OrderHub`. Hangfire dashboard + jobs.
**Done:** the full hero-GIF flow works end to end in one sitting. *(These three workstreams are parallelizable — reorder freely.)*

### Phase 6 — Hardening, docs, deploy · Weeks 12–14 (36 h)
HybridCache + tag invalidation; rate limiting; index review with `EXPLAIN ANALYZE`; N+1 sweep. OpenTelemetry + Aspire dashboard + business metrics. Bulk CSV import. Playwright ×3 + `e2e.yml`. Dockerfiles, `docker-publish.yml`, deploy to the free stack, migrations-at-release, `ResetDemoDataJob`, keep-warm cron. **README + hero GIF + Excalidraw C4 diagram + all 13 ADRs.**
**Done:** a stranger clones the repo, runs two commands, and it works; the live demo is up with seeded data.

### Weeks 15–16 — Buffer (assume you need it), then only if free: batch/expiry FEFO, OTP login, Aspire AppHost, the YARP artifact, Azure Bicep.

### Critical path
```
P0 → P1 (identity, Result, CI, test harness)
       └→ P2 (catalog + TENANCY + storage + search)      ← tenancy lands here or never
             ├→ P3 (SPA + OpenAPI codegen)
             └→ P4 (pricing → reservation → idempotency → outbox → state machine)
                   └→ P5 (payments ⟂ admin ⟂ SignalR)
                         └→ P6 (perf, obs, e2e, deploy, docs)
```

### Cut list — cut from the top when you slip
1. Batch/expiry FEFO (~8 h — low-stock alerts already carry the inventory-maturity signal)
2. OTP/phone login (~8 h + an SMS provider — email auth already demonstrates the token design)
3. Bulk CSV import (~10 h — the best of the cut-list items; cut #1 and #2 first to save it)
4. Extra Hangfire jobs (keep reservation expiry, outbox, slot generation — the pattern is demonstrated)
5. Prometheus `/metrics` (Serilog + OTel traces + health checks carry observability alone)
6. Third store + reviews moderation UI (two stores prove tenancy)
7. Payment reconciliation, then refunds (cut refunds only if payments risk not landing at all)
8. Playwright down to one spec (the pickup flow) — integration tests do the real work
9. Azure Bicep

**Never cut — these *are* the project:** server-side pricing · inventory reservation + the concurrency test · idempotency · pickup-vs-delivery · multi-tenancy + isolation test · ProblemDetails contract · refresh-token rotation · README + hero GIF + ADRs · `docker compose up` working from a clean clone.

> **The trap:** breadth (10 half-built features) instead of depth (5 built to production standard with tests that prove it). A 4–6 YOE reviewer scans for one thing — *does this person's code behave correctly under concurrency, failure, and hostile input?* Phase 4 answers that. Everything else is context around it.

---

## 10. Documentation & presentation

~8% of the effort, ~40% of the outcome. Budget real time in Phase 6, not "the last night."

**README, in this order:**
1. **One-line pitch + hero GIF (≤30 s, above the fold):** browse → add 500 g loose tomatoes → cart shows *pickup ₹487 vs delivery ₹526* → book a 6–7 pm pickup slot → pay (Razorpay test) → split screen: staff marks Ready, customer's screen live-updates with the pickup code. If someone watches only this, they should already believe you.
2. **Live demo link + demo credentials table** (customer / store staff / platform admin) + "demo data resets nightly at 03:00 IST".
3. **The problem, in your words** — kirana stores vs Blinkit/Zepto; pickup-or-delivery as the differentiator.
4. **Architecture diagram** — C4 Level 2, drawn in **Excalidraw** (hand-drawn reads as *made by a human who understands it*), plus a Mermaid version so it renders inline on GitHub.
5. **Run it locally in two commands** — `cp .env.example .env && docker compose up`.
6. **Engineering highlights** — 6 bullets, each linking to the *file* and the *test*.
7. **"What this replaced"** — the legacy audit table.
8. **Tech decisions** — table linking each choice to its ADR.
9. **What I'd do next / known limitations** — single instance, no SignalR backplane, no ES, no self-serve onboarding. *Naming your own limitations accurately is the strongest single seniority signal in a README.*

**ADRs** — `docs/adr/NNNN-*.md`, MADR format, written **as you decide** (15 minutes each when the reasoning is fresh):
`0001` rewrite vs strangler · `0002` Postgres over Mongo · `0003` vertical slices inside Clean Architecture · `0004` dependency governance (no MediatR/AutoMapper/FluentAssertions — licensing) · `0005` `Result<T>` over exceptions · `0006` no generic repository · `0007` reservation + conditional UPDATE · `0008` Razorpay over Stripe, plus COD · `0009` Postgres FTS over Elasticsearch · `0010` single-DB multi-tenancy · `0011` migrations at release · `0012` Hangfire over hosted services · `0013` .NET 10 LTS.

> `0004` and `0009` are the two most likely to make an interviewer sit up, because both are decisions **against** the obvious choice with a written rationale.

**API docs:** `Microsoft.AspNetCore.OpenApi` (built into .NET 10 — Swashbuckle is no longer in the default template) + **Scalar** as the UI. Then **close the loop: generate the frontend client from that OpenAPI doc** with `@rtk-query/codegen-openapi` in CI, failing the build if the committed client is stale. End-to-end type safety from the C# handler signature to the React prop, with zero hand-written TS API types. Top-tier signal, one evening.

**Résumé bullets** (numbers, no adjectives):

> **GroceryEasy — multi-tenant grocery commerce platform** · *ASP.NET Core (.NET 10), PostgreSQL, EF Core, React/TypeScript, Redis, Docker*
> - Rewrote a Node/MongoDB monolith into a Clean-Architecture .NET 10 API over PostgreSQL, closing 20 audited defects — including forgeable order totals and a bypassable admin guard — by moving pricing, authorization, and inventory decisions server-side; documented the rewrite-vs-strangler tradeoff in ADRs.
> - Built server-authoritative order placement with atomic inventory reservation, idempotency keys, and a transactional outbox; verified with a Testcontainers integration test in which 20 concurrent orders against 10 units of stock yield exactly 10 successes and zero ledger drift.
> - Shipped the product differentiator — customer-chosen store pickup vs. delivery — with capacity-bounded time slots, pincode serviceability, differential pricing, hashed pickup codes, Razorpay webhook-verified payments with daily reconciliation, and live order status over SignalR.

---

## 11. Verification

Each phase has a hard gate. Nothing advances until its gate is green in CI, not just locally.

| Phase | How to verify |
|---|---|
| 0 | `docker compose up` from a clean clone → all containers healthy; `cd legacy-node && npm start` still serves the old app |
| 1 | `dotnet test` green in CI on a fresh runner: the auth lifecycle test (register→verify→login→refresh→**replay old refresh token→family revoked**) and the NetArchTest suite both pass |
| 2 | `dotnet run --project GroceryEasy.Migrator -- seed demo --stores 3` then `GET /api/products?q=amool` returns Amul products via the trgm fallback; the store-A-reads-store-B test returns **404**; `EXPLAIN ANALYZE` shows the GIN index in use |
| 3 | `pnpm dev` → browse, search, register, verify, log in, refresh past access-token expiry without a re-login; `pnpm codegen && git diff --exit-code` clean |
| 4 | The 20-way concurrency test passes (exactly 10 succeed, `on_hand = 0`, ledger sums to 0); the idempotency test yields one order for two identical POSTs and 422 for key-reuse-with-different-body; place a pickup order and a delivery order end to end with COD in the browser |
| 5 | Razorpay test card → order `Confirmed` **only after** the webhook lands; a tampered signature returns 401 with no state change; two browsers side by side show the SignalR pickup-code push; the full hero-GIF flow completes in one sitting |
| 6 | Fresh clone on a **different machine**, `cp .env.example .env && docker compose up`, app reachable — no manual steps; Playwright green in `e2e.yml`; the deployed demo link works from a phone on mobile data |

**Continuous, every PR:** `ci.yml` must stay green — `dotnet format --verify-no-changes`, `build -warnaserror`, unit + Testcontainers integration tests, `tsc --noEmit`, ESLint, Vitest, `vite build`. Branch protection on `main` enforces it.

---

## 12. Phase 0 — Execution brief · Week 1 (~12 h)

**Goal:** the old app is frozen, tagged, and still runnable; the repo root is cleared for the new stack; the infrastructure the next 13 weeks depend on comes up healthy with one command; and the three decisions everything else rests on are written down.

**Nothing in Phase 0 is .NET code.** Not one `.cs` file. If you find yourself opening Visual Studio this week, you've skipped ahead — Phase 1 needs a clean foundation or you'll be fighting the repo layout for three months.

### Verified starting state

```
ECOMMERCE-MERN/          180 tracked files, branch main, no tags
├── backend/             the Express API
├── frontend/            the CRA app
├── ecommerce images/    50 old product images (shoes, cameras, phones) — wrong domain
├── package.json         ← THIS IS THE BACKEND'S package.json (start: node backend/server.js)
├── package-lock.json
├── Procfile             web: node backend/server.js   (Heroku leftover)
├── render.yaml          autoDeploy: true, branch main
├── .gitignore
├── .vscode/settings.json
└── node_modules/        the backend's deps (gitignored)
```
Remote: `github.com/YuvrajGitHub21/ECOMMERCE-MERN.git`. `frontend/build` and `backend/config/config.env` are correctly gitignored and untracked.

---

### Task 0.1 — Freeze the legacy app (~2 h)

**Move the whole legacy app as one unit.** Two CWD/relative-path couplings make any split break it:
- `backend/app.js:43,46` → `path.join(__dirname, "../frontend/build")`
- `backend/app.js:13` → `dotenv.config({ path: "backend/config/config.env" })` — resolved against **process CWD**, not `__dirname`

Moving `backend/` and `frontend/` to different parents breaks both and forces edits to code we just promised to freeze. Moving them together preserves every internal path, so **zero lines of legacy code change**.

```bash
# from repo root, on a clean tree
rm -rf node_modules frontend/node_modules        # gitignored; renaming them on Windows is slow

mkdir legacy-node
git mv backend            legacy-node/backend
git mv frontend           legacy-node/frontend
git mv package.json       legacy-node/package.json
git mv package-lock.json  legacy-node/package-lock.json
git mv Procfile           legacy-node/Procfile

git rm -r "ecommerce images"    # 50 wrong-domain images (shoes/cameras/phones); still in history
```

`git mv` renames the directory on disk, so the untracked-but-present `backend/config/config.env` travels with it. **Confirm it survived** — that file is your local Mongo connection string and is not in git:
```bash
ls legacy-node/backend/config/config.env
```

Root keeps only `.gitignore`, `render.yaml`, `.vscode/`. Then rewrite `.gitignore` for the new paths and add the entries the new stack needs:

```gitignore
# legacy (frozen)
legacy-node/node_modules
legacy-node/frontend/node_modules
legacy-node/frontend/build
legacy-node/backend/config/config.env

# .NET
[Bb]in/
[Oo]bj/
*.user

# new frontend
frontend/web/node_modules
frontend/web/dist

# local env
.env
.env.local
```

**Verify the frozen app still runs**, then tag it:
```bash
cd legacy-node && npm install && npm run dev      # boots, connects to Mongo, serves the CRA build
cd ..
git add -A && git commit -m "chore: freeze legacy MERN app into legacy-node/"
git tag -a v1-legacy-node -m "Final state of the college-era MERN app, before the .NET rewrite"
git push origin main --follow-tags
```

The tag matters beyond sentiment: `docs/legacy-audit.md` links every defect to a GitHub permalink pinned at this tag, so those links never rot as the repo changes around them.

> **Rule from here on: `legacy-node/` is read-only.** No fixes, no tidying, no "while I'm in here". Its value is as an honest before-picture.

**`render.yaml`** — its `buildCommand`/`startCommand` assume `package.json` at root, which is now false. **Decision: leave it untouched and let it break.** It stays at root, knowingly broken, and is deleted at the Phase 4 cutover alongside `Procfile`.

> One practical footnote: `autoDeploy: true` means Render retries and fails on **every push for the next 14 weeks**, emailing you each time. Turn off auto-deploy or suspend the service in the Render dashboard — a UI toggle, no code change, and it keeps the decision cost-free instead of merely deferred.

**`.gitattributes`** — add it now, before any new file exists. CI runs on Linux and you're on Windows; without this you get CRLF churn that pollutes every diff:
```gitattributes
* text=auto eol=lf
*.png binary
*.jpg binary
*.jfif binary
*.webp binary
```

---

### Task 0.2 — `docs/legacy-audit.md` (~3 h)

The single highest-leverage artifact in the project. Not a changelog — an argument that you can find your own bugs and design them out.

One entry per defect, all 20 from §2, in this shape:

```markdown
### L-01 · Order totals accepted from the client  ·  🔴 Critical

**Where:** [`backend/controllers/orderControllers.js:19-29`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/backend/controllers/orderControllers.js#L19-L29)

**What happens:** `newOrder` destructures `itemsPrice`, `taxPrice`, `shippingPrice`, `totalPrice`
and `orderItems[].price` from `req.body` and writes them to Mongo unchanged. No product is
looked up. `paidAt: Date.now()` is stamped unconditionally.

**Exploit:**
    POST /api/v1/order/new
    { "orderItems": [{ "product": "<real id>", "quantity": 500, "price": 0 }],
      "totalPrice": 1, "paymentInfo": { "id": "x", "status": "succeeded" } }
→ 201 Created. A paid order for 500 units at ₹1.

**Root cause:** the server has no opinion about price. It is a persistence layer for
whatever the browser says.

**Structural prevention:** `PlaceOrderCommand` carries no money fields at all — only
`cartId`, `fulfillmentType`, `slotId`, `addressId`. Totals come from `OrderPricingEngine`
over DB-read prices. `paidAt` is written only by the Razorpay webhook handler.
See [ADR-0005](adr/0005-result-over-exceptions.md) and plan §5.1.
```

Order them Critical → High → Medium, and open with a summary table (`ID · Title · Severity · Class`) so a reader gets the shape in ten seconds. Group by *class* in the summary — authorization, concurrency, input trust, dead code — because "I found 6 bugs that were all the same missing idea" is a stronger observation than "I found 20 bugs."

Close with a short **"What I'd do differently"** — three or four sentences, no self-flagellation. The tone that works: *this was written in second year, it worked, and here is precisely why it wouldn't survive contact with real money.*

---

### Task 0.3 — The first four ADRs (~2 h)

`docs/adr/`, MADR format, ~1 page each. Write them **now, before the code**, so they read as decisions rather than justifications.

| ADR | Title | The core of the argument |
|---|---|---|
| `0000` | Record architecture decisions | The meta-ADR (Nygard). Ten lines. Explains why the folder exists. |
| `0001` | Clean-room rewrite over strangler-fig | Strangler protects live traffic; there is none. It would cost ~3–4 weeks (gateway + cross-stack auth bridge + dual-write) of a 14-week budget on scaffolding that gets deleted. The seam is the frontend cutover instead. **Include the route order you *would* have used** — that's what shows you understand the pattern rather than dodging it. |
| `0002` | PostgreSQL over MongoDB | Orders, inventory, and payments are relational and need ACID. Names the specific capabilities being bought: transactions across order+inventory+outbox, `CHECK` constraints, FK integrity, versioned migrations, `tsvector` FTS, `numeric` money. Notes what's given up: schema flexibility, and the base64-in-document pattern (replaced by object storage). |
| `0013` | .NET 10 LTS over .NET 9 | .NET 9 was STS; support ended 2026-05-12. LTS runs to Nov 2028, so the project doesn't rot on your GitHub. Local SDK is already `10.0.204`. |

Add `docs/adr/README.md` as an index table. Each ADR ends with **Consequences** split into positive *and* negative — an ADR with no downsides listed reads as marketing and reviewers discount it.

---

### Task 0.4 — `docker-compose.yml` + `.env.example` (~3 h)

Anyone evaluating this repo tries `docker compose up` before reading a line of code. It must work from a clean clone on the first try.

| Service | Image | Ports | Purpose |
|---|---|---|---|
| `postgres` | `postgres:17-alpine` | 5432 | system of record |
| `redis` | `redis:7-alpine` | 6379 | HybridCache L2 |
| `mailpit` | `axllent/mailpit` | 1025 / 8025 | SMTP catcher + web UI (**not** MailHog — unmaintained since 2020) |
| `minio` | `minio/minio` | 9000 / 9001 | S3-compatible local object storage, same API as R2 |
| `minio-init` | `minio/mc` | — | one-shot: creates the `groceryeasy` bucket, then exits |
| `aspire-dashboard` | `mcr.microsoft.com/dotnet/aspire-dashboard` | 18888 | OTel traces/metrics/logs UI |

Requirements:
- **Healthchecks on postgres and redis**, with `depends_on: { condition: service_healthy }` — so the API in Phase 1 never starts against a database that isn't accepting connections yet. This is the single detail that makes `docker compose up` reliable instead of a coin flip.
- `postgres` gets an init script (`deploy/postgres/init.sql`) enabling `pg_trgm` and `citext`. Init scripts run only on an empty volume, so the extensions exist before EF Core's first migration in Phase 1.
- Named volumes (`pgdata`, `miniodata`) so a `docker compose down` doesn't wipe your data, and a documented `down -v` for when you want it to.
- **Profiles:** `core` (postgres + redis) for everyday work, `full` (everything) when you need mail/storage/telemetry. Keeps the laptop quiet.
- `.env.example` **committed**, `.env` **ignored**, and compose reads from `.env` with sane defaults inline.

Ship a short **ports table in the README** at the same time. Reviewers open the repo and want to know what's at `:8080` without reading YAML.

---

### Task 0.5 — Repo conventions (~2 h)

Small files, disproportionate signal — a reviewer sees these before any feature code.

- **`.editorconfig`** — C# (`csharp_style_*`, `dotnet_diagnostic` severities, file-scoped namespaces, `var` rules) and TS/JSON/YAML. Makes `dotnet format --verify-no-changes` in CI meaningful rather than noise.
- **`Directory.Build.props`** at `backend/` — applies to every project at once:
  ```xml
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <LangVersion>latest</LangVersion>
  ```
  `TreatWarningsAsErrors` from day one is the right call — it's painless now and unbearable to retrofit in week 10.
- **`Directory.Packages.props`** — `ManagePackageVersionsCentrally=true`. One place for every NuGet version across nine projects. Reviewers notice; most portfolio repos don't do it.
- **Conventional Commits** from the first commit (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`). Free, and it makes the git log itself a professional artifact.
- **Branch protection on `main`** (GitHub settings, manual): require a PR, require status checks once `ci.yml` exists in Phase 1. Work on `feat/*` branches and merge via PR **even though you're solo** — a history of reviewed PRs with green checks is evidence of habits, and it costs nothing.

---

### 12.6 — Decisions settled

1. **Render deploy: let it break.** `render.yaml` stays at root untouched and stops building; delete it with `Procfile` at the Phase 4 cutover. Suspend auto-deploy in the dashboard to stop the failure emails.
2. **Same repo, renamed.** Keep the full history — college code evolving into a .NET platform *is* the story. Rename `ECOMMERCE-MERN` → `groceryeasy` on GitHub; the redirect keeps old clone URLs working. Update the remote locally afterwards: `git remote set-url origin https://github.com/YuvrajGitHub21/groceryeasy.git`.
3. **`ecommerce images/`: removed.** `git rm -r` in Task 0.1. Still in history, so clone size is unchanged — this is purely tidiness.

### 12.7 — Definition of done

Phase 0 is complete when all six pass:

1. `docker compose up -d` → `docker compose ps` shows postgres and redis **healthy**, minio bucket created, Mailpit UI at `:8025`, Aspire dashboard at `:18888`
2. `cd legacy-node && npm install && npm start` → the old app boots, connects to Mongo, and serves the storefront **with zero source edits**
3. `git tag` lists `v1-legacy-node`, pushed to origin
4. Repo root contains only: `legacy-node/`, `docs/`, `deploy/`, `.gitignore`, `.gitattributes`, `.editorconfig`, `docker-compose.yml`, `.env.example`, `.vscode/`, and the deliberately-broken `render.yaml`
5. `docs/legacy-audit.md` covers all 20 defects, every file link resolving against the `v1-legacy-node` tag
6. `docs/adr/` has 0000, 0001, 0002, 0013 plus an index

**The real test — do it on Sunday night:** clone the repo fresh into a different folder, `cp .env.example .env`, `docker compose up`. It will fail the first time. Fixing that failure *is* Task 0.4.

---

## Critical files

Frozen or replaced, but they define the contract being ported and the defects being designed out:

- [backend/controllers/orderControllers.js](backend/controllers/orderControllers.js) — source of bugs #1, #6, #7; its request/response shape defines what the `PlaceOrder` slice replaces
- [backend/models/productModel.js](backend/models/productModel.js) — the Mongo document the Migrator reads; drives the `products`/`product_variants`/`product_images`/`reviews` split
- [backend/models/userModel.js](backend/models/userModel.js) — maps to Identity + `addresses`; source of bug #8 and the `pinCode as Number` type error
- [backend/middleware/error.js](backend/middleware/error.js) — returns `{success, error}`; the contract replaced by ProblemDetails and the root cause of bug #12
- [backend/utils/appFeature.js](backend/utils/appFeature.js) — the regex/operator injection surface replaced by Postgres FTS
- [frontend/src/App.js](frontend/src/App.js) — full route inventory (and bug #4); the parity checklist for `frontend/web`
- [render.yaml](render.yaml) and [Procfile](Procfile) — deleted in Phase 4 at cutover
