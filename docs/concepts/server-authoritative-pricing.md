# Server-authoritative pricing: no money fields in the contract vs. validating client totals

## The criteria that actually matter for this decision

This isn't "should the server trust the client" in the abstract — every backend developer says no to that question. The interesting part is *how* you say no, and the criteria that separate a strong answer from a weak one:

- **Whether the bug class can be represented at all**, not just whether a check catches it on the day it's written. A field that exists in a contract is a field every future code path can misuse; a field that doesn't exist can't be.
- **Where the trust boundary actually sits.** Money values either originate inside the trust boundary (a database read, inside the handler) or cross it (arrive in a request body) — the boundary itself, not a check placed near it, is what determines whether an attacker-controlled value can ever reach persistence.
- **Discipline load on future contributors.** A rule that must be *remembered and re-applied* on every new code path (an admin override, a partner API, a batch import) degrades over time. A rule enforced by the shape of the contract does not.
- **Auditability of what was actually charged.** Once an order is placed, does the record of "what the customer paid" stay fixed, or can a later price edit rewrite history?

## Comparison at a glance

| Criterion | Chosen: no money fields, fresh recompute | Validate client totals against recomputed | Signed price-quote token | Trust the client (legacy, `L-01`) |
|---|---|---|---|---|
| Can a wrong price ever reach persistence | No — there is no field to carry it | Only if the comparison is skipped or buggy on one path | Only if signature/expiry verification is skipped or the signing key leaks | Always — that *is* the mechanism |
| Attack surface | None — no price parameter exists in the command | A comparison that must run correctly on every path, forever | Crypto verification, key management, clock-skew/expiry handling | The entire request body |
| Extra moving parts | None | A recompute-and-compare step | Token issuance endpoint, signing key rotation, expiry policy | None |
| Legitimate use case | The default for any cheap-to-compute price | Detecting a stale cart, at best — see below | Prices that are expensive or external to compute and must be locked while the user reviews them (flight/hotel fares, FX quotes) | Never |
| Handles a price changing mid-checkout | Always reflects the current price | Only surfaces a mismatch; doesn't prevent the field existing | Explicit, by design (the quote has a TTL) | N/A — it never checks |

## Why not each alternative — the technical case

### Validate client-supplied totals against server-recomputed ones

The most credible-sounding alternative, because it *sounds* like "don't trust the client" done correctly. It isn't, for a specific reason worth being precise about:

- The type system still permits an attacker-supplied price to exist in memory. A comparison discarding it at runtime is a **defense** placed near the bug; removing the field is **structural elimination** of the bug class itself — the difference between "we check for this" and "this cannot be represented." This is the general software-security teaching point underneath the whole decision: a bug class survives exactly as long as the shape that admits it does.
- Every future code path that touches the command — an admin manual-order-entry screen, a partner integration, a CSV bulk-import — inherits a field that *must* be re-validated correctly, or it silently regresses to exactly `L-01`. That's a discipline problem, structurally identical to "remember to add the authorization attribute" (`L-05`) — same missing idea, different subsystem.
- Once you validate and find a mismatch, the only sound behavior is to reject and recompute anyway — which means the transmitted price was never actually *used* for anything except triggering an error path. If it can never legitimately win, it didn't need to exist.
- **Where it's genuinely useful, and the honest reason it still loses:** surfacing a "price changed since you added this to your cart" UX message *is* a real, legitimate feature. But that UX doesn't require transmitting money — a `cartVersion` or `priceCheckedAt` timestamp gives the same signal without ever putting a price value in an attacker's hands. Even the best-faith use case for this alternative doesn't actually need money fields in the contract.

### Signed price-quote token

Not named in the source material, but a real pattern worth knowing the boundaries of — it's how flight search, hotel booking, and FX conversion APIs legitimately let a client "carry" a price.

- Legitimate where the price is **expensive or external to compute** and must be locked for a review window the client controls — a fare that came from a GDS call, a rate that came from a forex provider. Re-deriving it on every request either isn't possible (the upstream quote expires) or is too slow to do per-request.
- `OrderPricingEngine` here is a pure function over a DB read of `product_variants.sale_price` — sub-millisecond, deterministic, no network call. A signed token buys nothing a fresh recompute doesn't already give for free, while adding a second code path, a signing key to manage and rotate, and clock-skew/expiry logic to get right.
- **Where it would win:** if pricing here involved a third-party rate service that was itself expensive or rate-limited to call, locking a quote would become the right tool. It isn't, so it's rejected on fit, not on principle.

### Trust the client (the legacy baseline, `[L-01]`)

Not a real alternative under consideration — the counter-example that motivates the whole decision, worth stating precisely because it's what the exploit actually looked like:

```http
POST /api/v1/order/new
{ "orderItems": [{ "product": "<any real id>", "quantity": 500, "price": 0 }],
  "itemsPrice": 0, "taxPrice": 0, "shippingPrice": 0, "totalPrice": 1,
  "paymentInfo": { "id": "anything", "status": "succeeded" } }
```

`newOrder` destructured `itemsPrice`, `taxPrice`, `shippingPrice`, `totalPrice`, and `orderItems[]` straight out of `req.body` and wrote them to Mongo unchanged — no product was ever loaded, no total was ever recomputed. Any authenticated user could place a "paid" order for 500 units at ₹1. The root cause wasn't a missing check on one field; it was that the server had no opinion of its own about price at all — it was a persistence layer for whatever the browser said.

## In GroceryEasy

`PlaceOrderCommand` carries **no money fields at all** — only `cartId`, `fulfillmentType`, `slotId`, `addressId`. Prices are read from `product_variants.sale_price` inside the handler and run through `OrderPricingEngine`, a pure, unit-tested function that extracts GST from inclusive prices (Indian retail convention), splits CGST/SGST, and rounds with `MidpointRounding.AwayFromZero` at 2dp, reconciling the grand total to the sum of lines. `order_items` stores a `unit_price_snapshot` so a later catalogue price edit can never rewrite what a customer was actually charged, and `paid_at` is written **only** by the Razorpay webhook handler — never by the order-creation path itself. There is no code path in the system that accepts a price from a request body.

See [`D1` in `docs/engineering-decisions.md`](../engineering-decisions.md#d1-server-authoritative-pricing-l-01) for the formal record, and [`L-01` in `docs/legacy-audit.md`](../legacy-audit.md#l-01) for the full exploit writeup this decision closes.

## Interview questions

**Q: Why not just validate the client's total against the server's recomputed total? That's also "not trusting the client."**
Because the field still exists in the contract, and a field that exists is a field every future code path can misuse — an admin screen, a partner API, a bulk importer, all now have to remember to re-validate correctly, forever. Removing the field means there's no code path that *can* accept a price, not one that's merely checked. That's the difference between a defense and structurally eliminating the bug class.

**Q: Doesn't removing the price field lose the ability to warn a user "the price changed since you added this to your cart"?**
No — that UX doesn't need a transmitted price at all. A `cartVersion` or `priceCheckedAt` signal gives the same "something changed, please confirm" behavior without ever putting a money value in the client's hands. Even the best-faith reason to send a price turns out not to need one.

**Q: Isn't recomputing the full price server-side on every checkout wasteful compared to trusting a cached client value?**
`OrderPricingEngine` is a pure function over a handful of already-loaded rows — sub-millisecond. There's no real cost being traded away; "cache it client-side" only sounds cheaper because it ignores that the cached value is now attacker-controlled.

**Q: Where would a signed price-quote token actually be the right call, and why doesn't this project need one?**
It's the right tool when a price is expensive or external to recompute per request — a flight fare from a GDS, an FX rate from a provider — and needs to be locked for a review window. Grocery pricing here is a local DB read plus arithmetic; there's nothing expensive to lock, so a token would add key management and expiry logic to solve a problem that doesn't exist.

**Q: What's the concrete exploit this closes?**
`POST /order/new` in the legacy app accepted `itemsPrice`, `taxPrice`, `totalPrice`, and per-item `price` straight from the request body and wrote them to Mongo unchanged, with no product ever loaded server-side. Any authenticated user could place a 500-unit order at ₹1 by simply setting those fields in the JSON body — `L-01` in the audit.

**Q: How do you guarantee the price on the order record can never drift from what the customer actually agreed to pay?**
`order_items` stores a `unit_price_snapshot` written once at order creation from `OrderPricingEngine`'s output — a later catalogue price change can never retroactively alter what an existing order shows or was charged, because the order doesn't reference the live price, it stores its own.

**Q: Give another example in this codebase of the same "structural elimination vs. defense" idea.**
The order state machine (`D5`) is the same principle applied to status transitions: `Order` has private setters, so the only way to change status is `TransitionTo`, checked against a `FrozenSet` of legal transitions — an illegal transition isn't caught by a check scattered across call sites, it's a state that literally cannot be reached. Same idea as pricing: make the invalid thing unrepresentable, not merely detected.
