> **Status: live working document,** written before Phase 5 begins and meant to be edited as work lands. Decisions remain owned by [`docs/engineering-decisions.md`](../engineering-decisions.md) and [`docs/adr/`](../adr/README.md). See [`master-plan.md`](master-plan.md) §6.2, §6.7 and §6.8 for the reasoning this brief compresses.

# GroceryEasy — Phase 5 agent brief

**Scope:** Razorpay payments end to end, the admin console, background jobs on Hangfire, and live order updates over SignalR.
**Budget:** about 36 hours, weeks 10 to 12.
**Branch:** `feat/phase-5-payments-admin`.
**Depends on:** Phase 4 complete — orders, the state machine and the outbox must all exist and be green.

**These three workstreams are genuinely independent** and can be reordered freely. Payments is the highest risk, so start there; if it slips, the admin console and SignalR still land. This is the opposite of Phase 4, where the ordering was forced.

---

## 1. Hard constraints

All earlier constraints still apply. In addition:

1. **The webhook is the source of truth for payment. The client is never trusted.** The client-side signature check unblocks the user interface and confirms nothing.
2. **Webhook signatures are verified over the raw request body.** Deserializing and re-serializing changes the bytes and breaks the signature. This is the single most common webhook bug — use `EnableBuffering()` and leave a code comment saying why.
3. **`paidAt` and the transition to `Confirmed` happen in the webhook handler and nowhere else** `[L-01]`.
4. **Real payment secrets never enter the repository.** `.env.example` already has empty `Payments__Razorpay__KeyId`, `Payments__Razorpay__KeySecret` and `Payments__Razorpay__WebhookSecret`, and `Payments__Provider=Fake` as the default. Keep it that way. Test keys go in a local `.env`, which is git-ignored, and in repository secrets for continuous integration.
5. **Integration tests never call Razorpay.** They run against `FakePaymentGateway` behind `IPaymentGateway`.
6. **SignalR messages are sent from outbox handlers, never directly from a command handler.** A notification must never be sent for a transaction that rolled back.
7. **The Hangfire dashboard is behind `PlatformAdmin` authorization.** An open Hangfire dashboard is a remote code execution surface.

### Non-goals for Phase 5

Do not build: caching, rate limiting or OpenTelemetry (Phase 6) · bulk comma-separated-values import (Phase 6) · Playwright tests (Phase 6) · deployment (Phase 6) · one-time-password phone login (cut list).

---

## 2. Workstream A — Payments (about 14 hours)

Read [`docs/concepts/payment-gateway-choice.md`](../concepts/payment-gateway-choice.md). Razorpay is chosen over Stripe for a stated technical-adjacent reason: Stripe India requires a registered business entity to onboard, so a working demonstration is not obtainable, while Razorpay issues test keys to an individual immediately — and the whole domain, rupees, tax, pincodes, kirana stores, is Indian. **Cash on Delivery stays a first-class method**; a kirana application without it is not credible.

### Task 5.1 — The gateway abstraction and the fake (about 2 hours)

`IPaymentGateway` in Application: create a provider order, verify a client signature, verify a webhook signature, issue a refund, list payments for reconciliation. `FakePaymentGateway` in Infrastructure, selected when `Payments__Provider=Fake`, and it is what every test uses.

**Acceptance:** the full Phase 4 order flow still passes with the fake gateway selected.

### Task 5.2 — Order creation and client verification (about 3 hours)

1. `POST /api/orders` puts the order in `PendingPayment`. The server calls the Razorpay Orders interface with `amount = grand_total × 100` **taken from the pricing engine**. Because the amount never comes from the request, `[L-01]` is unreachable by construction.
2. The browser checkout handler posts back the signature triple. The server verifies `HMAC_SHA256(order_id + "|" + payment_id, key_secret)` with a **constant-time comparison**.
3. That verification **only unblocks the user interface.** It does not confirm the order. Say so in a code comment, because the next person to read it will assume otherwise.

**Acceptance:** a tampered client signature is rejected and leaves the order in `PendingPayment` with no state change.

### Task 5.3 — The webhook (about 4 hours)

`POST /api/webhooks/razorpay`:

- Verify `X-Razorpay-Signature` over the **raw body**, with `EnableBuffering()`.
- Insert into `payment_events` keyed on the provider event identifier, which is unique. **A unique-constraint violation means the event was already processed — return 200 immediately.** That is the whole idempotency mechanism, and it needs no lock.
- Handle `payment.captured`, `payment.failed` and `refund.processed`.
- On capture: commit the inventory reservations, set `paid_at`, and transition the order to `Confirmed` through the state machine.
- The endpoint is anonymous but signature-gated, and rate-limited.

**Acceptance:** three integration tests — a valid event confirms the order; a tampered signature returns 401 with no state change; the same event identifier delivered twice produces exactly one side effect.

### Task 5.4 — Refunds and reconciliation (about 3 hours)

Refunds are issued on admin cancellation, recorded in a `refunds` table, and driven through the state machine's `Cancelled` transition, which already raises a refund event.

A **daily reconciliation job** compares Razorpay's payment list against the `payments` table and raises an alert for any order marked paid with no provider record, or any provider payment with no local order. Roughly sixty lines, and very few portfolio projects can say they built payment reconciliation. Do not cut this before refunds; the cut list puts reconciliation above refunds precisely because refunds matter more to a working demonstration.

**Acceptance:** an injected mismatch is detected and reported by the job.

### Task 5.5 — Manual verification with real test keys (about 2 hours)

With Razorpay test keys in a local `.env`, place an order, pay with a test card, and confirm the order only reaches `Confirmed` **after the webhook lands** — not after the client callback. Use the Razorpay dashboard's webhook replay or a tunnel to deliver it locally. Record what you did in the report; this is the check that catches a webhook that was never actually wired up.

---

## 3. Workstream B — Background jobs (about 6 hours)

### Task 5.6 — Hangfire (about 6 hours)

Read [`docs/concepts/background-jobs-strategy.md`](../concepts/background-jobs-strategy.md). `Hangfire.PostgreSql`, chosen over a bare `IHostedService` because jobs survive restarts, retry with backoff, and the dashboard is a screenshot for the README. ADR-0012.

**Migrate the two interim `IHostedService` timers from Phase 4** — expired-reservation release and slot generation — onto Hangfire and delete the `TODO` comments they were left with.

| Job | Cadence |
|---|---|
| `OutboxDispatcherJob` | every 5 seconds |
| `ReleaseExpiredReservationsJob` | every minute |
| `GenerateFulfillmentSlotsJob` | daily 00:30 India Standard Time, seven days ahead |
| `LowStockDigestJob` | daily 08:00 India Standard Time |
| `PaymentReconciliationJob` | daily 02:00 India Standard Time |
| `MarkNoShowPickupsJob` | hourly |
| `AbandonedCartNudgeJob` | hourly |
| `PurgeIdempotencyKeysJob` | daily |

Hangfire's own tables live in a `hangfire` schema, kept separate from the application schema so Entity Framework migrations and Hangfire never collide.

Dashboard at `/hangfire`, behind `PlatformAdmin`.

**Acceptance:** the dashboard lists every recurring job with a next-execution time, and killing the process mid-job leaves the job to be retried rather than lost.

---

## 4. Workstream C — Real-time and the admin console (about 16 hours)

### Task 5.7 — SignalR (about 4 hours)

One `OrderHub` with two group shapes: `order:{orderId}` for the customer and `store:{storeId}` for staff.

Authentication uses the standard `OnMessageReceived` handler reading `access_token` from the query string, because WebSocket connections cannot set request headers. Note in the code that this is why, and that the token is short-lived.

**Messages are published from outbox handlers**, so a notification is never sent for a rolled-back transaction.

No backplane — this is a single instance. State in the README that a Redis backplane is the scale-out answer. Knowing you do not need it yet is the point.

**The demonstration moment, and the README animation:** two windows side by side — staff clicks "Ready for Pickup" and the customer's screen updates instantly, showing the pickup code.

**Acceptance:** two browser windows, one status change, instant update in the other.

### Task 5.8 — Pickup codes (about 2 hours)

On the transition to `ReadyForPickup`, generate six digits, store **only** an HMAC-SHA256 hash with a 24-hour expiry, and push the plaintext to the customer once, over SignalR and by email. Staff enter the code, it is compared in constant time, and the order moves to `PickedUp`. Rate-limited to five attempts per order.

**Acceptance:** a Domain unit test covers hashing, expiry and the attempt limit with a fake time provider. The plaintext code appears in the database nowhere.

### Task 5.9 — Admin console (about 10 hours)

In `frontend/web/`, behind the `StoreStaff` role, on shadcn data tables:

- **Orders board grouped by status,** with transitions that capture a reason. The reason is written to `order_status_history`.
- **Pickup-code verification** — the staff side of Task 5.8.
- Product creation, reading, updating and deletion with image upload through the presigned `PUT` built in Phase 2.
- Inventory adjustments, each writing a `stock_ledger` row.
- User list and review moderation.
- A low-stock badge fed by SignalR.

Every one of these screens calls an endpoint already protected server-side. The client-side role check is a convenience only — the legacy application's admin guard was a hardcoded `isAdmin={true}` on an inner route, so all nine admin pages rendered for any logged-in user `[L-06]`. The `[Theory]` from Phase 2 that asserts 403 for a Customer on every admin endpoint must be extended to cover every endpoint added here.

**Acceptance:** the admin authorization theory covers the new endpoints and passes.

---

### Task 5.10 — Decision records (about 1 hour)

| Number | Title | Concepts document |
|---|---|---|
| 0008 | Razorpay over Stripe, with Cash on Delivery as a first-class method | `payment-gateway-choice.md` |
| 0012 | Hangfire over bare hosted services for background work | `background-jobs-strategy.md` |

Move both from *Planned* to *Accepted* in `docs/adr/README.md`.

---

## 5. Definition of done

1. `dotnet build` and `dotnet test --solution backend/GroceryEasy.sln` clean, exit code 0.
2. A Razorpay test card takes an order to `Confirmed` **only after the webhook lands**, verified manually and recorded in the report.
3. A tampered webhook signature returns 401 with no state change.
4. The same webhook event identifier delivered twice produces exactly one side effect.
5. The reconciliation job detects an injected mismatch.
6. The Hangfire dashboard lists every recurring job and is unreachable without `PlatformAdmin`.
7. Two browsers side by side show the SignalR pickup-code push.
8. The admin authorization theory covers every admin endpoint, new ones included, and returns 403 for a Customer.
9. No plaintext pickup code exists in the database.
10. **The full hero-animation flow completes in one sitting:** browse, add a loose item, compare pickup against delivery, book a slot, pay, staff marks ready, customer sees the code.
11. ADR-0008 and ADR-0012 accepted; concepts documents reconciled.
12. No real secret is committed — check with `git log -p` for the key names.
13. Continuous integration green; `git diff v1-legacy-node -- legacy-node/` empty.

---

## 6. Known pitfalls

- **Re-serializing the webhook body breaks the signature.** The most common webhook bug there is. `EnableBuffering()`, read the raw stream, verify, then deserialize.
- **Razorpay amounts are in paise**, so multiply by 100 and never pass a decimal. An off-by-100 error here is a live-money bug in a real system.
- **Webhook delivery is at-least-once and out of order.** A `payment.captured` event can arrive before your own response to the create call has been persisted. Handle both orderings.
- **The Hangfire dashboard is remote code execution if left open.** Authorize it.
- **Hangfire's schema and Entity Framework migrations.** Keep Hangfire in its own schema, or a `Respawn` reset in the test harness will wipe its tables and produce baffling failures.
- **SignalR in `WebApplicationFactory`** needs the test server's handler; the default HTTP client will not upgrade to a WebSocket connection. Budget time for this or test the hub through its outbox handler instead.
- **Constant-time comparison** for both the payment signature and the pickup code. `==` on strings leaks timing information.
- **A `[Theory]` over admin endpoints only protects what it enumerates.** Add the new routes to the list in the same commit that adds the endpoints.

---

## 7. Reporting back

Same protocol. For this phase specifically, state how the webhook was delivered during manual verification — replay, tunnel or otherwise — and confirm explicitly that the order was still `PendingPayment` after the client callback and only became `Confirmed` once the webhook arrived.
