> **Status: live working document,** written before Phase 4 begins and meant to be edited as work lands. Decisions remain owned by [`docs/engineering-decisions.md`](../engineering-decisions.md) and [`docs/adr/`](../adr/README.md). See [`master-plan.md`](master-plan.md) §5, §6.1 and §7 for the reasoning this brief compresses.

# GroceryEasy — Phase 4 agent brief

**Scope:** the server-side cart, pickup versus delivery fulfilment, the pricing engine, inventory reservation, idempotency, the transactional outbox, the order state machine, order placement, and the checkout user interface.
**Budget:** about 36 hours, weeks 8 to 10.
**Branch:** `feat/phase-4-ordering-spine`.
**Depends on:** Phases 2 and 3 complete.

---

## 0. Read this before anything else

**This is the phase the whole project is judged on.** A reviewer with four to six years of experience scans a portfolio repository for one question: *does this person's code behave correctly under concurrency, failure and hostile input?* Phase 4 is the answer. Everything else is context around it.

**Nothing here parallelises.** The order is forced by data dependencies:

```
cart → fulfilment (slots, serviceability) → pricing engine → inventory reservation
     → order aggregate + state machine → idempotency → outbox → endpoints → user interface
```

Attempting the order aggregate before the pricing engine means writing the money fields twice. Attempting reservations before slots means the two cannot share a transaction, which is a correctness requirement, not a preference.

**Build the pricing engine test-first.** It is a pure function with no input or output and no dependency injection, so there is no excuse not to, and the rounding cases are genuinely easier to get right from the tests inward.

---

## 1. Hard constraints

All earlier constraints still apply. In addition, and these are the ones that matter:

1. **`PlaceOrderCommand` carries no money fields at all.** Only `cartId`, `fulfillmentType`, `slotId` and `addressId`. Every rupee comes from the pricing engine over prices read from the database. This is the structural fix for `[L-01]`, where the legacy controller wrote client-supplied `itemsPrice`, `taxPrice` and `totalPrice` straight to the database and stamped `paidAt` unconditionally, so `POST /order/new` with `totalPrice: 1` was persisted as a paid order.
2. **`paidAt` is written by the payment webhook handler and by nothing else.** Not by the order endpoint, not by the client, not "temporarily for testing".
3. **Stock never changes without a `stock_ledger` row in the same transaction.**
4. **Inventory reservations are applied in `variant_id` order,** enforced in one place — `InventoryRepository.TryReserveAsync` — with a test that proves it. Out-of-order application across concurrent multi-item orders is how this design deadlocks.
5. **`TimeProvider` everywhere.** Slot cutoffs, reservation expiry and pickup-code validity are all time-dependent and must be deterministically testable.
6. **Order status changes only through `order.TransitionTo(status, actor, reason)`.** `Order` has private setters. An illegal transition returns `Result.Failure`, never an exception and never a silent no-op.
7. **The public URL cuts over to the new single-page application at the end of this phase,** and `render.yaml` and `Procfile` are deleted then. Not before.

### Non-goals for Phase 4

Do not build: Razorpay or any real payment provider (Phase 5 — this phase ships Cash on Delivery only) · the admin console (Phase 5) · SignalR (Phase 5) · Hangfire (Phase 5 — see the note in Task 4.4 about how to run the release job in the meantime) · caching (Phase 6).

---

## 2. Task breakdown

### Task 4.1 — Server-side cart (about 4 hours)

Read [`docs/concepts/cart-storage-strategy.md`](../concepts/cart-storage-strategy.md). The cart lives in PostgreSQL as `carts` and `cart_items`. Not in `localStorage`, not in Redis as the source of truth. Redis holds only the derived badge count, and only in Phase 6.

- An anonymous cart is keyed by a cookie identifier; a user cart is keyed by user identifier.
- **On login, merge the anonymous cart into the user cart.** Decide and document the merge rule — summing quantities per variant is the sane default, capped at available stock.
- Cart items store `variant_id` and `quantity` only. **No price.** Prices are read fresh every time the cart is displayed, so a price change is reflected immediately rather than honoured from a stale client value.
- Loose-goods quantities are validated as a multiple of `step_qty` at the point of addition.

**Acceptance:** an integration test adds items anonymously, logs in, and asserts one merged cart with correct quantities.

---

### Task 4.2 — Fulfilment: hours, slots, serviceability (about 6 hours)

This is the product differentiator. A repository-wide search of the legacy code for `pickup|self.?pick|fulfillment|deliveryType` returns zero matches — the feature the product story rests on was never built. Build it fully, and build it first.

- **Slot generation.** A job materializes seven days of `fulfillment_slots` per store from `store_hours` minus `store_closures`: 60-minute pickup slots with capacity 8, 90-minute delivery slots with capacity 4. Materializing rows rather than computing availability on the fly is what makes capacity booking atomic.
- **Booking** uses the same conditional-update trick as inventory:
  ```sql
  UPDATE fulfillment_slots SET booked_count = booked_count + 1
   WHERE id = @id AND booked_count < capacity
  ```
  Zero rows affected **is** the `SlotFull` answer. No read-then-write window, no lock, no retry loop.
- **Slot booking and inventory reservation share one transaction.** You can never hold stock without a slot, or a slot without stock.
- **Cutoffs.** A slot disappears once `now > slot_start - lead_time`, with lead time 30 minutes for pickup and 60 for delivery, computed in `Asia/Kolkata` through `TimeZoneInfo` and `TimeProvider`. Timezone-correct slot logic is a detail interviewers poke at, so test it across an India Standard Time day boundary with a fake time provider.
- **Serviceability.** `GET /api/stores/{id}/serviceability?pincode=411001` returning fee, minimum order, free-above threshold and estimated time. **The cart screen checks it before the customer reaches checkout,** not after — that is the difference between a product and a tutorial.

Until Hangfire arrives in Phase 5, run slot generation from a plain `IHostedService` timer or a manual endpoint behind `PlatformAdmin`, and leave a `TODO` naming Phase 5. Do not add Hangfire early.

**Acceptance:** two concurrent bookings of the last slot produce exactly one success and one `SlotFull`. Slot cutoff behaviour is tested with a fake time provider across an India Standard Time midnight.

---

### Task 4.3 — The pricing engine, written test-first (about 5 hours)

Read [`docs/concepts/server-authoritative-pricing.md`](../concepts/server-authoritative-pricing.md).

`GroceryEasy.Domain/Ordering/OrderPricingEngine.cs` — a pure function, no input or output, no dependency injection:

```
Price(IReadOnlyList<PricedLine> lines, FulfillmentChoice choice, StoreConfig cfg) -> OrderPricing
```

`PricedLine` is built by the handler from the database — `variant.sale_price` and `product.gst_rate` — never from the request.

The rules, each of which gets its own test:

- **Goods and Services Tax is India-correct.** Indian retail prices are tax-**inclusive**, so tax is *extracted*, not added: `tax = line_total - (line_total / (1 + rate))`, split evenly into central and state components for an intra-state sale. Getting this right is a domain-credibility signal; getting it backwards is immediately visible to any Indian reviewer.
- **Loose goods:** quantity must be a multiple of `step_qty`.
- **Delivery fee:** zero for pickup; otherwise the pincode's fee, waived above the free-delivery threshold. Test the boundary at exactly the threshold value, not near it.
- **Pickup discount:** two percent by default, capped. This is the differentiator made visible in money.
- **Rounding is decided once.** Banker's rounding is wrong for currency, so use `MidpointRounding.AwayFromZero` at two decimal places, and reconcile the grand total against the sum of the lines so it cannot drift by a paisa. A dedicated rounding test suite is a strong signal — include the half-paisa cases.
- **`pricing_version` is stamped on every order,** so a later change to the engine never retroactively alters an old invoice.

**Acceptance:** 100 percent line coverage of the engine. `grand_total` equals the sum of the lines in every test, including the rounding cases.

---

### Task 4.4 — Inventory reservation (about 5 hours)

Read [`docs/concepts/inventory-concurrency-strategies.md`](../concepts/inventory-concurrency-strategies.md). This is ADR-0007 and it is a decision worth being able to defend in three sentences.

This is the structural fix for the two critical concurrency defects: `[L-03]`, where stock was never checked or reserved when an order was placed, and `[L-04]`, where the decrement ran as an unawaited `forEach` whose rejection called `process.exit(1)`.

The chosen mechanism is a reservation table with a time-to-live plus an atomic conditional update, with `xmin` optimistic concurrency kept for admin edits only:

```sql
UPDATE inventory SET reserved = reserved + @qty
 WHERE variant_id = @id AND on_hand - reserved >= @qty
```

PostgreSQL serializes writers on the row internally, so **zero rows affected is the out-of-stock answer**, with no read-then-write window, no explicit lock, no retry loop and no deadlock — provided items are applied in `variant_id` order.

Be able to say why the two alternatives lost. `SELECT ... FOR UPDATE` deadlocks when two concurrent multi-item orders lock overlapping rows in different sequences; sorting fixes it but makes correctness depend on every future caller remembering to sort. Pure `xmin` on inventory is correct but livelocks on a hot stock-keeping unit, re-running the whole handler on each retry.

The flow:

1. `POST /api/checkout/start` reserves every line and books the slot **in one transaction**, writes ledger rows, and sets `expires_at = now + 15 minutes`.
2. `POST /api/orders` creates the order in `PendingPayment`.
3. Payment capture (Phase 5) commits the reservations: `on_hand -= qty`, `reserved -= qty`. For Cash on Delivery in this phase, commit at order confirmation.
4. A job releases expired holds every 60 seconds. Until Hangfire arrives, an `IHostedService` timer is acceptable — leave a `TODO` naming Phase 5.

**The test that sells this in an interview,** and it goes in the README by name: 20 concurrent `PlaceOrder` calls against a variant with `on_hand = 10` yield **exactly 10 successes and 10 `OutOfStock` results, `on_hand` ends at 0, and the ledger sums to zero drift.**

**Acceptance:** that test passes, reliably, ten runs in a row. A flaky concurrency test is worse than none.

---

### Task 4.5 — Idempotency (about 3 hours)

Read [`docs/concepts/idempotency-keys.md`](../concepts/idempotency-keys.md).

An `IdempotencyBehavior` in the dispatcher pipeline, applied to commands marked `IIdempotentCommand`. The client sends `Idempotency-Key` as a version-7 uuid.

- `INSERT INTO idempotency_keys (...)` — **a unique-constraint violation is how the duplicate is detected**, without taking a lock.
- Completed: replay the stored response verbatim.
- In flight: return 409 with `Retry-After`.
- Same key with a different request body hash: return 422.

**The frontend generates the key once when the checkout screen mounts, not per click.** That is what collapses a double-click, a retry and a flaky-network resend into one order, and it is the structural fix for `[L-15]`, where the legacy cart was never cleared and a double submission produced duplicate orders.

**Acceptance:** the same key posted twice yields one order and two identical response bodies; the same key with a different body yields 422.

---

### Task 4.6 — Transactional outbox (about 3 hours)

Read [`docs/concepts/transactional-outbox.md`](../concepts/transactional-outbox.md).

The failure it prevents is concrete and demonstrable: the order commits, the process crashes, and the customer never learns their order exists.

- A `SaveChanges` interceptor collects domain events from tracked aggregates into `outbox_messages` **inside the same transaction** as the state change.
- A dispatcher job claims a batch with **`FOR UPDATE SKIP LOCKED`** — the correct pattern and worth knowing by name — dispatches, applies exponential backoff, and dead-letters after five attempts.
- Roughly 150 lines. The ADR should note that `LISTEN/NOTIFY` or a message broker is the scale answer, and that this is deliberately not that.

In this phase the only consumers are order-confirmation email and a low-stock alert. SignalR joins them in Phase 5. **Nothing sends a notification directly from a handler**, ever — that is precisely the failure mode being designed out.

**Acceptance:** an integration test forces the transaction to roll back after the outbox row is written and asserts no message was dispatched.

---

### Task 4.7 — Order aggregate and state machine (about 4 hours)

Read [`docs/concepts/state-machine-pattern.md`](../concepts/state-machine-pattern.md).

`orders` carries `order_number` in the form `GE-2026-000123` from a PostgreSQL sequence, `fulfillment_type`, `slot_id`, `pickup_code_hash`, the full price breakdown, `pricing_version` and `xmin`. `order_items` carry **snapshot columns** — name, variant, unit price, tax rate — so a later catalogue price edit never rewrites order history.

Legal transitions live in a `static readonly FrozenSet<(OrderStatus, OrderStatus)>` and **branch on fulfilment type**:

```
Common:   Draft -> PendingPayment -> Confirmed
          PendingPayment -> Expired | PaymentFailed
          Confirmed -> Cancelled   (only while not yet Packed; raises a refund event)

Pickup:   Confirmed -> Packing -> ReadyForPickup -> PickedUp -> Completed
                                  ReadyForPickup -> NoShow   (job, slot end plus 4 hours)

Delivery: Confirmed -> Packing -> OutForDelivery -> Delivered -> Completed
                                  OutForDelivery -> DeliveryFailed -> OutForDelivery
```

Every transition writes an `order_status_history` row recording who, when and why, and raises a domain event that reaches the outbox.

**Acceptance:** a `[Theory]` over the **full** legal and illegal transition matrix, both fulfilment branches. Not a sample of it — the full matrix. This is a pure Domain test with no mocks.

---

### Task 4.8 — Endpoints (about 3 hours)

| Method | Route | Notes |
|---|---|---|
| POST | `/api/checkout/start` | reserves stock and books the slot in one transaction; returns the priced summary |
| POST | `/api/orders` | idempotent; **no money fields in the request body** |
| GET | `/api/orders` | the current user's orders |
| GET | `/api/orders/{id}` | resource-based `OrderOwnerOrStoreStaff` policy |

`GET /api/orders/{id}` closes `[L-16]`, where the legacy route was admin-only so users could not see their own orders, and `[L-05]`, the pattern of missing ownership checks. Authorization is a resource-based `IAuthorizationHandler`, not an `if` statement in the handler.

**Acceptance:** user A requesting user B's order gets 404, not 403 — consistent with the tenancy rule from Phase 2, and for the same reason: 403 confirms the resource exists.

---

### Task 4.9 — Checkout user interface (about 5 hours)

- **The cart comparison screen — this single screenshot is the product vision.** Render both options side by side:
  ```
  Pickup    ₹0 delivery    −2% pickup discount   Ready in 45 min     → ₹487.20
  Delivery  ₹29 delivery   free above ₹499       Arrives 6–7:30 pm   → ₹526.00
  ```
  Serviceability is checked here, on the cart screen, before checkout.
- Slot picker grouped by day, showing capacity and hiding slots past their cutoff.
- Address selection and creation.
- Order summary, with Cash on Delivery as the only payment method in this phase.
- Order detail with a status timeline built from `order_status_history`. The legacy equivalent was an eleven-line stub `[L-20]`.

The idempotency key is generated **once when the checkout screen mounts** and reused for every submission attempt from that screen.

**Acceptance:** a pickup order and a delivery order both placed end to end in a real browser with Cash on Delivery.

---

### Task 4.10 — The integration test suite (about 3 hours)

Beyond the tests named in earlier tasks:

- Place an order: happy path for pickup, happy path for delivery, out of stock, slot full, non-serviceable pincode, empty cart, closed store.
- The 20-way concurrency test from Task 4.4.
- The idempotency pair from Task 4.5.
- The outbox rollback test from Task 4.6.
- The full transition matrix from Task 4.7.
- Order ownership: 404 for another user's order.

**Acceptance:** all green in continuous integration on a fresh container, not only locally.

---

### Task 4.11 — Cutover (about 1 hour)

Only once everything above is green:

- Point the public URL at the new single-page application.
- `git rm render.yaml Procfile`.
- Update the README to describe the new application as the live one.
- `legacy-node/` stays in the repository, still frozen, still tagged. It is the before-picture and it is not deleted.

**Acceptance:** the public URL serves the new application; `git diff v1-legacy-node -- legacy-node/` still returns empty.

---

### Task 4.12 — Decision records (about 1 hour)

ADR-0007, inventory concurrency with a reservation table and an atomic conditional update, moved from *Planned* to *Accepted*, paired with `docs/concepts/inventory-concurrency-strategies.md`. Confirm that sections D1 through D6 of `docs/engineering-decisions.md` match what was built, and that `idempotency-keys.md`, `transactional-outbox.md`, `state-machine-pattern.md`, `server-authoritative-pricing.md` and `cart-storage-strategy.md` still describe the real implementation.

---

## 3. Definition of done

1. `dotnet build` and `dotnet test --solution backend/GroceryEasy.sln` both clean, exit code 0.
2. **The 20-way concurrency test passes ten consecutive runs:** exactly 10 successes, `on_hand = 0`, ledger sums to zero.
3. The idempotency test yields one order for two identical requests and 422 for the same key with a different body.
4. The pricing engine has 100 percent line coverage, and `grand_total` equals the sum of the lines in every case.
5. The full state-machine transition matrix passes for both fulfilment branches.
6. A pickup order and a delivery order are placed end to end in a browser with Cash on Delivery.
7. Two concurrent bookings of the last slot yield one success and one `SlotFull`.
8. Another user's order returns 404.
9. The outbox rollback test proves no message is dispatched for a rolled-back transaction.
10. The public URL serves the new single-page application; `render.yaml` and `Procfile` are deleted.
11. ADR-0007 accepted; the concepts documents reconciled.
12. Continuous integration green on the pull request.
13. `git diff v1-legacy-node -- legacy-node/` returns empty.

---

## 4. Known pitfalls

- **A flaky concurrency test is worse than no concurrency test.** Run it ten times before claiming it passes. If it is flaky, the design is wrong, not the test.
- **Testcontainers and connection pool exhaustion.** Twenty parallel requests against a small pool will look like a concurrency bug and be a configuration problem. Size the pool deliberately and say what you set it to.
- **Nested transactions.** The existing `TransactionBehavior` already opens one for commands, and `IApplicationDbContext.HasActiveTransaction` exists so the checkout handler can join rather than open a second — Entity Framework Core rejects the second. Read that interface before writing checkout.
- **India Standard Time is UTC+05:30.** A half-hour offset breaks naive slot arithmetic in ways a whole-hour offset does not. Test across midnight India Standard Time specifically.
- **Extracting tax versus adding it.** `total * rate` is the wrong formula for a tax-inclusive price and will be visibly wrong to anyone in India.
- **The `xmin` concurrency token needs explicit Entity Framework configuration** as a row version. It does not work by default.
- **`FOR UPDATE SKIP LOCKED` needs a real transaction.** Inside Entity Framework this usually means raw SQL — check the generated statement rather than assuming.
- **Do not add Hangfire in this phase.** Two `IHostedService` timers with `TODO` comments are the correct interim answer; adding the scheduler early spreads Phase 5 work across two phases.

---

## 5. Reporting back

Same protocol. In addition, for this phase specifically: paste the **ten consecutive runs** of the concurrency test, and state the connection pool size used. Those two facts are what make the claim credible.
