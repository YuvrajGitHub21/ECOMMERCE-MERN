# Inventory concurrency: atomic conditional `UPDATE` vs. the alternatives

## The criteria that actually matter for this decision

Reserving stock is a write against a shared row from multiple concurrent transactions — a textbook concurrency-control problem, but the criteria that decide it are specific to what checkout actually does:

- **No lost updates, ever.** Two concurrent orders for the last unit must never both succeed. This is the direct, structural fix for `L-04`, where a read-modify-write with no guard let `Stock` go negative under concurrent shipments.
- **No deadlock on multi-item orders.** A real cart has several line items touching several inventory rows in one transaction; the strategy has to be safe when two such transactions overlap on different rows in different orders.
- **Graceful degradation under contention on a hot SKU**, not just correctness in the uncontended case. The one item that's nearly out of stock is exactly the item multiple customers are racing for — the strategy has to behave well precisely where it matters most, not just pass a single-threaded test.
- **No read-then-write window.** Any strategy with a separate "read the current state" step followed by a later "write" step has a gap where the world can change in between — that gap is where `L-04` lived.
- **Minimal new infrastructure.** Postgres is already the transactional source of truth (`A3`); a strategy that requires a second system to stay consistent is a strategy with a second failure mode.
- **Auditability.** When something does go wrong, can drift be detected, or is it invisible until a customer complains? `L-03` and `L-04` were both invisible in production for exactly this reason.

## Comparison at a glance

| Criterion | Chosen: reservation table + atomic conditional `UPDATE` | `SELECT ... FOR UPDATE` (pessimistic) | Optimistic concurrency (`xmin`/version) | Application-level distributed lock (Redis) |
|---|---|---|---|---|
| Prevents lost updates | Yes — check and write are one atomic statement | Yes, while the lock is held | Yes, via retry-on-conflict | Yes, if the lock is implemented correctly |
| Deadlock risk on multi-item orders | None, if write order is centralized (it is — one repository method) | Real, unless every caller sorts lock acquisition consistently | None — no locks held | None — but lock *acquisition* order across resources can still deadlock |
| Behavior under hot-SKU contention | Degrades gracefully — losers get a normal `OutOfStock` result, no retry | Serializes; later transactions queue behind the lock holder | Retry storm — every collision re-runs the whole handler, throughput can collapse under enough concurrency | Depends on the lock's own contention behavior; adds latency for every request regardless of contention |
| Read-then-write window | None — the `WHERE` clause is evaluated by the same statement that writes | None *within* the lock, but the lock is acquired in a separate step from the decision | Real — read version, decide, write, hope it hasn't changed | Real, structurally the same shape as pessimistic locking, plus network latency to the lock service |
| Extra infrastructure | None — native Postgres | None | None | A second system (Redis) that must itself stay available and consistent |
| Where it's actually right | High-contention writes behind a single relational store | Short, simple critical sections with a *provably* consistent lock order | Low-contention, human-paced edits (kept here for admin edits) | Coordinating a resource that ISN'T behind one transactional database |

## Why not each alternative — the technical case

### `SELECT ... FOR UPDATE`

The textbook answer to "how do I reserve stock," and the one most interviewers expect first — worth taking seriously before rejecting it.

- **Concrete deadlock:** Order A wants `[Milk, Bread]` and locks `Milk` first, then tries to lock `Bread`. Order B, placed a moment later, wants `[Bread, Milk]` and locks `Bread` first, then tries to lock `Milk`. A now waits on the row B holds; B now waits on the row A holds. Neither can proceed — Postgres detects the cycle and kills one transaction after a deadlock timeout, which just becomes a failed order that still needs a retry.
- The standard fix — always acquire locks in a consistent order (e.g., sort line items by `variant_id` before locking) — genuinely works, but it means **correctness now depends on every future caller remembering to sort**, with nothing in the type system enforcing it. That's the same discipline problem `D1` and `L-05` keep running into: a rule that has to be re-applied correctly on every new code path eventually isn't.
- It also holds the row lock for the **entire remaining transaction**, including whatever business logic runs between the `SELECT` and the `COMMIT` — pricing calculation, other DB round-trips, anything the handler does before it's done. Longer lock hold time means more transactions queue behind it under contention, exactly where graceful degradation matters most.
- **The honest overlap:** the chosen design *also* requires applying items in a consistent (`variant_id`) order to avoid multi-item deadlocks — that requirement doesn't disappear. What changes is *where* the discipline lives: with `FOR UPDATE` exposed to any handler that wants a lock, the ordering rule has to be remembered everywhere it's used. Here, inventory writes go through exactly one method — `InventoryRepository.TryReserveAsync` — so the ordering is enforced and tested in one place instead of trusted to every call site. Same requirement, but centralized into something a test can actually cover.

### Optimistic concurrency (version column / Postgres `xmin`)

Correct, lock-free, and the right default for most concurrent-edit problems — genuinely comparable to the chosen approach on several axes, which is why it isn't rejected outright here, just for this specific use.

- Under contention on a hot SKU — the exact scenario that matters at checkout — every losing transaction gets a concurrency-conflict exception and must retry the **entire handler**: re-read, recompute price, resubmit. As concurrency rises, retries stack on top of newly arriving requests, and throughput can degrade into a retry storm or outright livelock rather than failing gracefully.
- A checkout collision (`OutOfStock`) is a normal, expected business outcome under load — customers really do compete for the last unit of a low-stock item — and `Result<T>` (`C5`) already models expected failures without an exception-driven retry loop. Optimistic concurrency forces exactly the retry-and-recheck dance that `Result<T>` was chosen to avoid.
- **Kept deliberately for admin edits** — this is the honest near-tie in this decision. An admin editing a product or adjusting stock is a single human, at human pacing, with near-zero contention; "someone else changed this since you loaded it, please refresh" is *exactly* the right UX there, and a version check is simpler than reasoning about atomic `UPDATE ... WHERE` semantics for a low-stakes, low-frequency edit. Same primitive, different fit: low contention favors optimistic, high contention on a shared hot resource favors the chosen approach.

### Application-level distributed lock (e.g., Redis-based)

Not named in the source ADR, but the alternative most engineers reach for once they've heard "avoid `FOR UPDATE`" — worth ruling out explicitly.

- It solves a problem Postgres already solves for free: **row-level write serialization is intrinsic to the database.** Two concurrent `UPDATE` statements against the same row are automatically ordered by the engine itself — the second simply waits for the first transaction to finish, then evaluates its own `WHERE` clause against the now-current value. Putting a Redis lock in front of that is coordinating a second system to protect a resource the first system already protects atomically.
- It adds a new failure mode that has to be reasoned about independently: what happens if the process holding the lock crashes before releasing it (needs a TTL/lease), and what happens if the TTL expires while the process is still working and a second process acquires the "same" lock (needs fencing tokens to avoid two processes both believing they hold exclusive access — the well-known critique of naive Redlock-style locking). None of that complexity buys anything here, because the invariant being protected already lives entirely inside one Postgres row.
- **Where it would be the right tool:** coordinating something that *isn't* behind a single transactional database — serializing a distributed cron job across multiple app instances, or rate-limiting calls to an external non-transactional API. Not "protect a row that Postgres already owns and already serializes."

## The chosen solution — why "zero rows affected" is elegant

```sql
UPDATE inventory SET reserved = reserved + @qty
 WHERE variant_id = @id AND on_hand - reserved >= @qty
```

The check and the write are the **same statement**. There is no moment where the application has read "8 available" and is deciding what to do with that fact while another transaction does the same — the `WHERE` clause is evaluated by Postgres, against the current row, at the instant the write happens. Concurrent writers targeting the same row are serialized by Postgres's own MVCC/row-locking machinery: the second `UPDATE` simply blocks until the first transaction commits or rolls back, then its `WHERE` clause is evaluated against whatever the row looks like *now* — not against a value read moments earlier. If two transactions both try to take the last 5 units of a 5-unit stock, one commits and the other's `WHERE` clause evaluates to false against the updated row: **zero rows affected**, which *is* the out-of-stock signal, with no exception, no retry, no separate check.

The reservation table adds a TTL on top of that atomic primitive because checkout is a multi-step flow with a slow, external step in the middle (payment) — you must never hold a lock or an open transaction across an HTTP call to a payment provider. Reserving stock, releasing it automatically if payment doesn't complete within the window, and only decrementing `on_hand` on confirmed payment keeps the atomic write fast and short-lived while still handling abandoned checkouts correctly. Every movement writes an append-only `stock_ledger` row, so drift is detectable by reconciliation rather than invisible — the direct fix for `L-03` (stock never checked at all) and `L-04` (an unawaited, non-atomic decrement that could take the whole process down and still go negative).

**Proof:** an integration test fires **20 concurrent orders at 10 units of stock**. Exactly 10 succeed, 10 return `OutOfStock`, `on_hand` lands on 0, and the ledger sums to zero — the direct, load-bearing demonstration that no lost update is possible under real concurrent traffic, not just in a single-threaded unit test.

## In GroceryEasy

See [`D2` in `docs/engineering-decisions.md`](../engineering-decisions.md#d2-inventory-concurrency--conditional-update--adr-0007-phase-4) for the formal record. ADR-0007 (inventory concurrency) is listed as **planned, not yet written** in [`docs/adr/README.md`](../adr/README.md) — the design is settled and recorded in engineering-decisions.md; the formal ADR document itself lands with the Phase 4 implementation. This decision closes [`L-03`](../legacy-audit.md#l-03) (stock never checked at order time) and [`L-04`](../legacy-audit.md#l-04) (an unawaited, non-atomic decrement that could go negative and crash the process).

## Interview questions

**Q: Why not `SELECT ... FOR UPDATE`? Isn't that the standard way to reserve a row?**
It deadlocks under real multi-item traffic: if order A locks Milk then Bread, and order B — placed moments later — locks Bread then Milk, each ends up waiting on a row the other holds. The fix (sort lock acquisition order) works, but it means correctness depends on every future caller remembering to sort, forever. It also holds the lock for the whole transaction, including business logic that runs after the read, which hurts exactly under the contention this needs to handle well.

**Q: You said the chosen solution also needs consistent ordering across items to avoid deadlocks — so how is it actually better than `FOR UPDATE`?**
The requirement doesn't disappear, but where it's enforced changes completely. Inventory writes go through exactly one method, `InventoryRepository.TryReserveAsync`, so the ordering rule is centralized and covered by a test — not a discipline every handler that happens to touch inventory has to remember independently. Same rule, one enforcement point instead of many.

**Q: Why not optimistic concurrency with a version column — it's lock-free and simpler to reason about?**
Because under contention on a hot SKU — the exact case that matters at checkout — every losing transaction has to retry the entire handler: re-read, recompute price, resubmit. Under enough concurrency that becomes a retry storm instead of graceful degradation. It's genuinely the right tool for low-contention, human-paced edits, which is why it's kept for admin product/stock edits — just not for checkout.

**Q: When do you actually use optimistic concurrency in this project, then?**
Admin edits — a single staff member editing a product or adjusting stock, where contention is near zero and "someone else changed this, please refresh" is the correct UX for a human-paced edit. It's a genuine near-tie with the checkout strategy; the deciding factor is contention level, not correctness.

**Q: Why not a Redis lock — isn't that the standard way people handle inventory or ticket sales at scale?**
Because it solves a problem Postgres already solves for free: row-level writes to the same row are automatically serialized by the database's own MVCC machinery. A Redis lock adds a second system that has to stay consistent with the first, plus its own failure modes — a process crashing while holding the lock, or a TTL expiring mid-operation and a second process acquiring the "same" lock. It's the right tool for coordinating something that *isn't* behind one transactional database, not for protecting a row Postgres already owns.

**Q: Walk me through exactly what happens when 20 concurrent orders hit 10 units of stock.**
Each order's reservation runs `UPDATE inventory SET reserved = reserved + qty WHERE variant_id = @id AND on_hand - reserved >= qty` inside its own transaction. Postgres serializes the 20 writers on that row internally — one at a time, each `UPDATE`'s `WHERE` clause is evaluated against the row as it stands at that moment. The first 10 (by qty of 1 each, say) succeed and consume the stock; from the 11th onward, `on_hand - reserved >= qty` is false, so the statement affects zero rows, which the handler reads as `OutOfStock`. End state: exactly 10 successes, 10 `OutOfStock` results, `on_hand` at 0, and the ledger sums to zero.

**Q: Why a separate reservation table with a TTL instead of just decrementing `on_hand` directly when the order is placed?**
Checkout has a slow external step in the middle — the payment call — and you must never hold a lock or an open transaction across an HTTP round-trip to a third party. Reserving stock lets checkout hold "this is spoken for" state that expires automatically if payment never completes, while the atomic write itself stays fast and short-lived. It also separates "reserved" from "sold" for reporting, which a bare decrement can't express.
