# Idempotency keys: unique-constraint-as-detector vs. check-then-insert

## What the pattern solves, generally

A network call has three possible outcomes, not two: it succeeds, it fails cleanly, or **the client never finds out which one happened** — the request timed out, the tab closed, a proxy reset the connection after the server had already committed the write. A client that retries on "I don't know" is the entire point of resilient clients, and it is also exactly how a `POST /orders` gets executed twice for one real purchase.

An **idempotency key** breaks the ambiguity: the client attaches an opaque token to the request representing "this one attempt to do this one thing." The server's contract becomes *"the same key, presented any number of times, produces the same result exactly once"* — the first attempt does the real work, every subsequent attempt with the same key gets back the stored result of the first, without redoing it. This is the same mechanism Stripe and Razorpay expose to merchants for exactly this reason: payment APIs are the textbook case where "did that already happen?" is an unacceptable question to have no answer to.

## The criteria that actually matter for this decision

- **One mechanism has to cover three distinct causes of duplication** — a double-click, an explicit user retry after a visible error, and a client-library or proxy silently resending a request the user never saw fail. Handling only one of these (e.g. disabling the submit button, which only stops the double-click) leaves the others open.
- **No race window between "check if this happened" and "record that it's happening."** Two requests carrying the same key can arrive close enough together that any non-atomic check-then-act sequence can let both through.
- **No new lock-management infrastructure**, for the same reason `D2` (inventory concurrency) avoided one: Postgres is already the transactional source of truth, and a second coordination mechanism is a second failure mode.
- **A closed answer for "same key, different body."** A key is supposed to represent one specific attempt at one specific cart; silently accepting a replay with different contents would corrupt the guarantee.

## Comparison at a glance

| Criterion | Chosen: unique constraint, `INSERT`-as-detector | Naive: `SELECT` then `INSERT` | Distributed lock (Redis `SETNX`) |
|---|---|---|---|
| Race-free under concurrent identical requests | Yes — the constraint is enforced by the same statement that writes | No — classic check-then-act (TOCTOU) gap | Yes, if the lock is acquired and released correctly |
| Requires an explicit lock | No | Only if wrapped in `SERIALIZABLE` or `SELECT ... FOR UPDATE`, which reintroduces the deadlock/hold-time concerns from `D2` | Yes — a second system's lock, with its own crash/TTL semantics |
| New infrastructure | None — a unique index on an existing table | None | A second system (Redis) that must itself stay available |
| What "already seen" looks like | A unique-violation exception on `INSERT` | A row returned by `SELECT` | A failed `SETNX` |
| Failure mode if it's wrong | N/A — correctness is enforced by the database engine, not application logic | Two orders from one checkout click — exactly `L-15` | Lock never released (crash) → false "duplicate" forever, or TTL expiry mid-request → false negative |

## Why not the naive check-then-insert

The instinctive first design, and worth walking through exactly why it fails:

```sql
-- Request A                              -- Request B (same key, arrives 4ms later)
SELECT 1 FROM idempotency_keys            SELECT 1 FROM idempotency_keys
  WHERE key = @key;                         WHERE key = @key;
-- 0 rows → "not seen before"              -- 0 rows → "not seen before"
INSERT INTO idempotency_keys (key, ...);  INSERT INTO idempotency_keys (key, ...);
-- proceeds to create the order           -- proceeds to create the order
```

Both requests run their `SELECT` before either has run its `INSERT`, because neither has committed yet — so both legitimately see zero rows and both conclude, correctly *at the moment they checked*, that this is a new attempt. This is a **check-then-act (TOCTOU) race**, the same shape of bug as the legacy inventory decrement (`L-04`): a decision made from a snapshot that's stale by the time the write happens. A double-click fires two HTTP requests close enough together that this window is not a theoretical edge case — it is the primary scenario idempotency keys exist to prevent.

Closing this gap with `SELECT ... FOR UPDATE` or a `SERIALIZABLE` transaction is possible, but it means taking an explicit lock or accepting transaction-retry semantics for what should be one of the simplest writes in the system — reintroducing exactly the lock-hold-time and contention concerns `D2` chose to avoid for inventory (see [inventory-concurrency-strategies.md](inventory-concurrency-strategies.md)), for a problem that has a lock-free answer.

## The chosen solution — the `INSERT` *is* the check

```sql
CREATE TABLE idempotency_keys (
    key             uuid PRIMARY KEY,
    request_hash    bytea NOT NULL,
    response_body   jsonb,
    status          text NOT NULL DEFAULT 'processing'
);
```

The handler's first action is to `INSERT` the key, `request_hash`, and a `processing` status — before doing any real work. There is no separate "check" step:

- If the `INSERT` succeeds, this is genuinely the first time this key has been seen — safe to proceed, compute the result, and update the row with the final `response_body` and `status = 'completed'`.
- If the `INSERT` raises a **unique-violation** (Postgres `SQLSTATE 23505`, surfaced in .NET as a `PostgresException` with `SqlState == "23505"`), some other request already claimed this key. The handler catches that specific exception and, depending on the existing row's status, either replays the stored `response_body` verbatim (status `completed`) or returns a "still processing, retry shortly" response (status `processing`).
- If the same key arrives again with a **different** `request_hash` (a client bug reusing a stale key against a changed cart), the handler returns `422 Unprocessable Entity` rather than either creating a second order or silently replaying an unrelated result.

The reason no lock is needed: **the check and the write are the same operation.** Postgres's own unique index enforces exclusivity at the storage-engine level — two concurrent `INSERT`s targeting the same primary key are serialized by the database itself, and exactly one of them can win. There is no window between "look" and "act" because there is no separate "look" step to have a window. This is the same shape of fix as `D2`'s atomic conditional `UPDATE` for inventory: replace a read-then-decide-then-write sequence with one statement whose success or failure *is* the answer.

## The client-side design: one key per checkout mount, not per click

The `Idempotency-Key` is generated **once, when the checkout screen mounts** — a `crypto.randomUUID()` stored in component state — not regenerated on every "Place order" click. This single choice is what makes one mechanism cover three different causes of duplication at once:

- **Double-click.** Both click events fire against the same in-memory key, because the key was never tied to the click in the first place. Two near-simultaneous requests race on the same `INSERT`; the database resolves it as shown above.
- **Explicit retry.** The first attempt fails visibly (a timeout, a transient 500) and the customer clicks "Place order" again. The component hasn't unmounted, so the same key is still held in state — the retry is, from the server's point of view, indistinguishable from the double-click case, and resolves the same way: one order, one response.
- **Flaky-network resend.** An HTTP client's automatic retry logic, or a proxy replaying a request it isn't sure was received, resends the exact same HTTP request — same key, same body, same everything — with the user never seeing anything happen at all. Same mechanism, same outcome.

The one case this deliberately does **not** collapse: if the customer navigates away from checkout and comes back later (the component unmounts and remounts), a new key is generated. That is correct, not a gap — leaving and returning to checkout is a new, distinct intent to place an order, and should be treated as one. Tying the key to *mount*, rather than to the click, is precisely what draws that line in the right place: same component instance → same intent → same key; a fresh component instance → a fresh intent → a fresh key.

Generating a new key **per click** instead would defeat the pattern entirely: a double-click would produce two different keys, and two different keys are, by definition, two different orders — the exact bug this exists to close.

## In GroceryEasy

See `D3` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record. Closes [`L-15`](../legacy-audit.md#l-15) — the legacy checkout dispatched `createOrder` without awaiting it and navigated to `/success` regardless of the outcome, with no `CLEAR_CART` action anywhere in the codebase, so a customer who went back and resubmitted created a second identical order at a stale total. In GroceryEasy, `Cart.Clear()` runs inside the same database transaction that creates the order, so the two can never disagree, and the idempotency check guarantees that transaction runs at most once per checkout attempt.

## Interview questions

**Q: Why generate the idempotency key when the checkout screen mounts, instead of when the user clicks "Place order"?**
Because a per-click key can't catch anything except the click itself. Generating the key once per mount means a double-click, a manual retry after a visible failure, and a silent network-level resend all carry the *same* key — one mechanism collapses all three into "one attempt," rather than needing separate handling for each.

**Q: Walk me through the actual race in the naive check-then-insert approach.**
Two requests with the same key arrive close together. Both run `SELECT ... WHERE key = @key` before either has committed anything, so both legitimately see zero rows and both conclude "not seen before." Both then `INSERT` and both proceed to create an order. The check and the act are two separate steps with a gap between them, and two concurrent requests can both pass through that gap — the same shape of bug as the legacy unguarded stock decrement (`L-04`).

**Q: How does the unique-constraint approach avoid that race without taking a lock?**
The check and the write are the same statement: attempting the `INSERT` *is* the check. Postgres's own unique index serializes concurrent inserts on the same key at the engine level, so exactly one succeeds; the other gets a unique-violation exception it can catch and handle as "already in progress" or "already completed." There's no separate lookup step, so there's no window for two requests to both believe they're first.

**Q: What happens if the same idempotency key is replayed with a different order body?**
The server stores a hash of the original request alongside the key. A replay with a matching hash returns the original stored response. A replay with a *different* hash — the same key reused against a changed cart, which should never happen from a correct client but should never be trusted blindly either — returns `422 Unprocessable Entity` instead of either silently creating a second order or returning a result for the wrong request.

**Q: How does this relate to how you solved inventory concurrency in `D2`?**
Same underlying move: replace a read-then-decide-then-write sequence with a single atomic database operation whose outcome *is* the decision. `D2` uses "zero rows affected" from a conditional `UPDATE`; idempotency uses a unique-violation from an `INSERT`. Both avoid an explicit lock by relying on a guarantee Postgres already provides for free.
