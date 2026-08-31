# Payment gateway choice: Razorpay + COD vs. the alternatives

## The criteria that actually matter for this decision

Not "which payment provider is best" in the abstract — the properties that matter for *this*
project's actual constraints:

- **An individual, not a registered business, has to be able to get a genuinely working demo.**
  This is a solo portfolio project. A gateway that requires business registration to issue even
  test credentials fails the project's actual goal — a live, clickable, real payment flow —
  before a single line of integration code gets written.
- **Domain fit for the actual market being modelled.** The product is an Indian kirana/grocery
  app — rupees, GST, pincodes, neighborhood stores. A gateway (or a payment method) that's a poor
  fit for how Indians actually pay online is a domain-judgment gap, not just an accessibility one.
- **The legacy failure this replaces was total, not partial.** The old "payment" was a client-side
  `setTimeout` (`[L-02]` in [`legacy-audit.md`](../legacy-audit.md)) — no money moved, ever, and
  combined with the order-total bug (`[L-01]`), both the price *and* the paid status of an order
  were attacker-controlled. The bar isn't "better than that," it's "structurally cannot regress to
  that," which is a claim about *where trust is anchored*, not which SDK is used.
- **The client can never be the source of truth for "did the payment succeed."** Whatever gateway
  is chosen, the actual security property that matters is server-to-server confirmation,
  independent of anything the browser claims.

## Comparison at a glance

| Criterion | Razorpay | Stripe | PayPal |
|---|---|---|---|
| Individual (non-business) can get working test/live keys | Yes — sandbox keys issued immediately on signup | No — Stripe India requires a registered business entity to onboard | Difficult — similar business-registration friction in the Indian market |
| UPI support | First-class, native | Limited/indirect in India | Weak — PayPal is not a primary UPI rail |
| Local payment method coverage (India) | Cards, UPI, netbanking, wallets, EMI — built for this market | Card-first; India-specific methods are a secondary concern | Card/PayPal-balance first; not built around Indian rails |
| Realistic path to a genuinely working demo for this project | Yes | Likely blocked entirely | Likely blocked or degraded |
| Domain fit for "Indian kirana grocery app" | Direct — the product the gateway is built for | Generic, global-first | Generic, global-first |
| Webhook-based server-confirmed payment model | Yes | Yes | Yes |

## Why not each alternative — the technical case

### Stripe

The default answer for "which payment gateway," and the most credible peer on pure API quality —
worth being honest about that before explaining why it's still wrong for this project.

- Stripe's API design, documentation, and webhook model are excellent, arguably the industry
  reference implementation. If this project targeted a market where Stripe operates without
  friction, it would be a perfectly reasonable choice.
- The blocker is concrete and non-negotiable for this project's situation: **Stripe India requires
  a registered business entity** (GST registration, a business PAN, a current account) to
  complete onboarding, even to exercise anything beyond the most limited test mode. An individual
  building a portfolio project, with no registered business, cannot get Stripe into a state that
  demonstrates a real, working, end-to-end payment flow in the Indian market.
- A demo that can't actually process a payment isn't a smaller version of the feature — it's the
  feature not existing. The whole point of including payments in this project is to show a
  correct security model (webhook-verified, server-authoritative) working end to end; a
  gateway that can't be turned on defeats that before the security design even matters.
- **Where it would have won:** for a company already operating internationally, or an individual
  with an existing registered business, Stripe's global reach and polish would make it the
  stronger default. It loses here specifically on *accessibility to an individual in this market*,
  not on any technical merit of its API.

### PayPal

Worth naming briefly since it's the other reflexive answer, without spending as much time on it
as Stripe since the objections mostly rhyme.

- Similar onboarding friction for an individual in the Indian market — PayPal Business accounts
  in India also expect business documentation for full functionality, and consumer-side
  PayPal usage skews toward cross-border transactions rather than domestic Indian retail.
- Materially weaker support for the payment methods that actually matter in this market: PayPal
  is not a UPI participant in the way a UPI-native gateway is, and its India footprint is
  historically oriented around international remittance and cross-border e-commerce rather than
  a domestic kirana-store checkout flow.
- Doesn't meaningfully out-perform Stripe on any axis that matters here — it inherits the same
  accessibility problem without Stripe's compensating strength in API design, so it doesn't even
  win as "the fallback if Stripe is blocked."

## The chosen option: Razorpay + Cash on Delivery

### Why Razorpay, specifically

- **Issues sandbox/test API keys to an individual immediately** on signup, no business entity
  required to get a working integration in test mode — which is the one property that actually
  unblocks this project's goal of a real, demoable, end-to-end payment flow.
- **The entire product domain is Indian** — prices in rupees, GST-inclusive pricing extracted and
  split CGST/SGST, pincodes for serviceability, kirana/neighborhood store fulfilment. Razorpay is
  built around exactly this market: UPI as a first-class method (not bolted on), plus netbanking,
  cards, wallets, and EMI, all in the combination Indian shoppers actually use. This isn't a
  consolation choice next to Stripe — for a product whose whole domain is India, it's the more
  *domain-correct* choice on its own terms, not merely the more accessible one.

### Why include Cash on Delivery as a first-class method

- A grocery/kirana app that only accepts card or UPI is not a credible model of the Indian
  market — COD remains commonly used there, especially for lower-ticket, neighborhood-store-style
  purchases, which is exactly this product's positioning.
- Concretely small to build — roughly 30 lines wiring COD as an explicit `PaymentMethod` value
  that skips the gateway integration and marks the order `Confirmed` without a captured payment —
  but it's a genuine domain-judgment signal: it shows the design reflects how the target market
  actually transacts, not just "whatever the payment SDK's happy path demonstrates."
- It is handled as **its own explicit, typed payment method**, not as a fallback or an unverified
  default — which matters because "payment succeeded" for COD means something different (order
  confirmed, payment collected later) than it does for a captured Razorpay payment, and the order
  state machine (`D5` in [`engineering-decisions.md`](../engineering-decisions.md)) has to
  represent that distinction rather than paper over it.

## The core security design: the webhook is the only source of truth

This is the part of the decision that actually closes `[L-02]` — the legacy "payment" being a
`setTimeout` in the browser that unconditionally marked an order paid, compounded by `[L-01]`'s
client-supplied totals so that both the price *and* the paid status of an order were whatever the
attacker's request body said.

- **The client's success callback never marks anything paid.** When Razorpay's checkout widget
  reports success in the browser, that event only unblocks the UI (show a "processing" state,
  redirect toward the order page) — it triggers no state change on the server. Trusting a
  client-reported success event is structurally the same bug as the legacy `setTimeout`, just
  wearing a real SDK's clothing.
- **`POST /webhooks/razorpay` — a server-to-server callback from Razorpay's infrastructure, not
  the customer's browser — is the only path that can transition an order to `Confirmed`.** An
  order reaches a paid state exclusively because Razorpay's servers, not the shopper's browser,
  told this backend's servers so.
- **Signature verification runs over the raw request body**, not a re-serialized version of the
  parsed payload. This is the single most common webhook security bug across every provider that
  signs payloads (Stripe, Razorpay, GitHub, and others all document this pitfall explicitly): if
  the webhook handler deserializes the JSON into an object and then re-serializes it before
  computing the HMAC, whitespace, key ordering, or float formatting can change even though the
  *meaning* of the payload didn't — and the recomputed signature silently fails to match, or
  worse, a naive implementation skips verification "since parsing succeeded." The fix is
  mechanical and non-negotiable: capture the exact bytes Razorpay sent, compute the HMAC over
  those bytes before any JSON parsing happens, and only parse afterward once the signature is
  confirmed to match.
- **Idempotent by event id.** Every verified webhook event is recorded in a `payment_events` table
  keyed on Razorpay's own event id, so a redelivered webhook (which providers do on purpose, for
  reliability) is a no-op rather than a double-processed payment.
- **A daily reconciliation job** diffs Razorpay's own payment list against this system's recorded
  payments — a second, independent check that the webhook-driven state actually matches reality,
  catching the case where a webhook was missed entirely (network partition, an outage during the
  callback window) rather than merely malformed.

The general principle, statable in one line for an interviewer: **the client is allowed to
*initiate* a payment and *observe* its outcome, but it is never allowed to *assert* one.** Every
legacy payment defect was a version of letting the client assert something only the server (or in
this case, the payment provider's own backend) is positioned to know.

## In GroceryEasy

See `I1` in [`engineering-decisions.md`](../engineering-decisions.md) for the formal record — its
companion ADR-0008 is not yet written; when it lands it will hold the full context and
consequences. This closes
`[L-02]` in [`legacy-audit.md`](../legacy-audit.md) directly — the legacy "payment" was a
2-second `setTimeout` that unconditionally dispatched a fake success, and a commented-out Stripe
controller that would have crashed the server with `MODULE_NOT_FOUND` if ever uncommented, since
the `stripe` package was never even installed.

## Interview questions

**Q: Why not Stripe? It's the industry-standard payment API.**
It is, and its API design is arguably the best of the three. The blocker is onboarding: Stripe
India requires a registered business entity to get past the most limited test mode, and this is
an individual's portfolio project with no registered business behind it. A payment integration
that can't actually be exercised end to end isn't a lesser version of the feature — it's the
feature not existing. Razorpay issues real sandbox keys to an individual immediately, which is the
one property that made a genuine end-to-end demo possible at all.

**Q: Isn't picking Razorpay "just because it's easier to sign up for" a weak technical reason?**
It would be, if that were the whole reason — but it isn't. The product domain is entirely Indian:
rupees, GST-inclusive pricing, pincodes, kirana stores. Razorpay's UPI-first, India-native payment
method coverage is a better *domain* fit independent of the onboarding question — it's the gateway
built for the market this app models. Accessibility and domain-correctness happened to point the
same direction here, which is worth stating plainly rather than hiding behind one reason when both
are real.

**Q: Why does Cash on Delivery matter — isn't that a step backward from "real" digital payments?**
No — it's the opposite of a step backward, it's domain accuracy. COD remains a commonly used
payment method in the Indian market this product models, especially for the kind of
lower-ticket, neighborhood-store purchase this app represents. An app that only supports card/UPI
and has no COD option would read as someone who built a payments feature without checking how the
target market actually pays. It's about 30 lines of code as an explicit, typed payment method —
cheap to include and it's a real domain-judgment signal.

**Q: Walk me through what happens if a customer's browser reports payment success — what actually marks the order paid?**
Nothing the browser says marks anything paid. The success callback in the checkout widget only
updates the UI state — a "processing" screen, essentially. The order transitions to `Confirmed`
only when `POST /webhooks/razorpay` receives a verified `payment.captured` event directly from
Razorpay's servers. The signature is verified over the raw request body before any JSON parsing
happens, the event is recorded keyed on Razorpay's event id so redeliveries are no-ops, and a
daily reconciliation job cross-checks Razorpay's own payment list against what's recorded here.
The client can initiate and observe a payment; it can never assert one.

**Q: What's the most common mistake people make implementing payment webhooks, and how did you avoid it?**
Verifying the signature against a re-serialized version of the parsed JSON body instead of the
exact raw bytes that were received. Deserializing and then re-serializing can change whitespace,
key order, or number formatting even when the semantic content is identical, which means the
recomputed HMAC silently fails to match the one the provider sent — and a rushed implementation
tends to either "fix" that by skipping verification or by comparing the wrong thing. The webhook
handler here captures the raw request body first and computes the signature over those exact
bytes before any parsing occurs.

**Q: What's the concrete legacy bug this whole design replaces?**
`Payment.js` regex-validated a card number and CVC in the browser, then ran a `setTimeout` that,
after two seconds, dispatched a hardcoded `{ id: "mock_payment_id", status: "succeeded" }` and
navigated to a success screen — no money ever moved, and no server endpoint was ever called to
confirm anything. Combined with the order-total bug (`[L-01]`), an attacker controlled both the
price and the paid status of any order. There's no code path in the new design that lets a client
assert payment success; only a signature-verified webhook from Razorpay's own servers can.
