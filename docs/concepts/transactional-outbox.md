# Transactional outbox: outbox table + polling job vs. the alternatives

## The dual-write problem

Some operations need to change a database row *and* trigger a side effect in a completely different system — send a confirmation email, publish a domain event, broadcast over a websocket, call a webhook. The database (Postgres, ACID, transactional) and the side-effect system (an SMTP relay, a message broker, a SignalR hub) are two different systems with two different failure models, and there is no way to wrap "commit this row" and "call that other system" in one atomic operation. This is the **dual-write problem**, and it shows up in the same shape everywhere it's relevant, not just for orders:

- Commit the DB row, *then* call the side effect → the process can crash, time out, or lose network in the gap between the two calls. The row is committed; the side effect never fires.
- Call the side effect first, *then* commit the DB row → the side effect fires for a change that then fails to commit and never actually happened.

Neither ordering is safe, because the two systems don't share a transaction. The transactional outbox pattern solves this without needing the two systems to agree on anything: instead of calling the side effect directly, the handler writes a row describing *the intent to do the side effect* into an `outbox_messages` table, **in the same database transaction** as the state change. That INSERT either commits alongside the order or rolls back alongside it — atomicity is free, because it's now one transaction against one system (Postgres), not a distributed transaction across two. A separate background process then reads unprocessed outbox rows and actually dispatches them (sends the email, publishes the event), independently and asynchronously.

## The criteria that actually matter here

- **Is the side effect genuinely lossy if dropped?** "Customer never learns their order exists" is a real, demoable failure — not a cosmetic issue.
- **Does the fix need to be proportionate?** A pattern that adds a table, an interceptor, and a background worker is only worth it if the failure it prevents is real; reaching for it reflexively is its own mistake.
- **At-least-once is the realistic delivery guarantee**, not exactly-once — whatever pattern is chosen has to make peace with that, because true exactly-once delivery across two independent systems isn't achievable without cooperation neither system here provides.
- **Multiple workers must be able to process the queue concurrently without double-processing or blocking each other** — a background job that's single-instance-only doesn't scale and doesn't survive a worker restart mid-batch.

## Comparison at a glance

| Criterion | Fire directly in the handler | 2-phase commit / distributed transaction | Outbox + polling job (chosen) |
|---|---|---|---|
| Atomicity with the DB write | None — two separate, unrelated calls | True atomicity, if both participants support the protocol | Effectively atomic — the outbox row rides the same transaction as the state change |
| Works with an HTTP-based side effect (email API, webhook, SignalR) | Yes, trivially | No — HTTP calls can't participate in 2PC | Yes — dispatch happens later, out of band |
| Failure mode when the side-effect system is briefly down | Side effect silently lost | Whole transaction blocks/fails | Row stays in the outbox, retried with backoff |
| Delivery guarantee | Best-effort, none really | Exactly-once (in theory) | At-least-once |
| Operational cost | None | High — a transaction coordinator, XA-capable participants | Moderate — a table, an interceptor, a background job |
| Build effort | Trivial | Large, and rarely worth it | ~150 lines plus a polling job |

## Why not each alternative — the technical case

### No outbox — call side effects directly in the handler

- The exact failure this project needs to avoid: `Order` is committed inside `PlaceOrderCommandHandler`, then the handler calls `_emailService.SendAsync(...)`. The process crashes, times out, or the host recycles between those two lines — the order exists, the customer was never told. It doesn't have to be exotic; a routine deploy mid-request is enough.
- The reverse ordering isn't safer: call the email service first, then commit — now a commit failure (a constraint violation, a dropped connection) means the customer got told about an order that doesn't exist.
- "Wrap it in a try/catch and retry" doesn't actually fix it — the *retry state itself* needs to survive a crash to be meaningful, which means it needs to live somewhere durable. That's an outbox, just a hand-rolled and probably less correct one (no `FOR UPDATE SKIP LOCKED`-equivalent concurrency safety, no backoff, no dead-letter).
- Genuinely the right call for side effects that **aren't** lossy in a way that matters — an analytics ping, a non-critical log line. The mistake is applying "just call it inline" uniformly, including to the side effects where losing them is a real incident.

### Two-phase commit / distributed transaction across DB and broker

- The textbook "correct" answer: a transaction coordinator asks every participant (the database, the message broker) to *prepare* (vote to commit), then issues a second round telling all of them to actually commit or all of them to roll back — true atomicity across systems, when it works.
- Rarely used in practice for this shape of problem, for concrete reasons: it requires every participant to speak the XA protocol, which most side-effect targets simply don't — an HTTP-based email API, a webhook, or SignalR cannot participate in 2PC at all, full stop, regardless of how it's configured.
- Even where it's technically available (some message-queue + SQL-Server combinations support it), the operational cost is real: two network round-trips per participant instead of one, and a new failure mode where the coordinator crashes mid-protocol, leaving participants "in doubt" — holding locks, blocked, until someone manually resolves the transaction.
- **Where it would actually win:** a small number of participants that all natively support XA, in an environment that already pays the operational cost of a transaction coordinator for other reasons. Essentially nobody builds a new system's order-confirmation-email pipeline this way, which is exactly why it's worth naming and dismissing explicitly rather than ignoring.

## The chosen approach: outbox table + polling job with `FOR UPDATE SKIP LOCKED`

A `SaveChangesInterceptor` hooks into the same `DbContext.SaveChangesAsync` call that persists the order, and writes the relevant domain events (order placed, low-stock threshold crossed) as rows into `outbox_messages` — inside the exact same transaction. There is no separate "commit the outbox" step to fail independently; if the order commits, the outbox row committed with it, and if the order rolls back, so does the outbox row.

A background job (Hangfire, recurring every few seconds) claims a batch of unprocessed rows with:

```sql
SELECT * FROM outbox_messages
WHERE processed_at IS NULL AND attempts < 5
ORDER BY created_at
LIMIT 20
FOR UPDATE SKIP LOCKED;
```

`FOR UPDATE` locks the selected rows for the duration of the transaction, so no other worker can select the same rows concurrently. `SKIP LOCKED` is what makes this safe *and* fast with multiple concurrent workers: instead of the second worker blocking until the first worker's lock releases (which would serialize all workers on the same row set and defeat the point of having more than one), it simply skips any row already locked by another transaction and grabs the next available one. Without it, either every worker contends on the same rows (bad throughput) or the application needs to hand-roll claiming logic — a `claimed_by`/`claimed_at` column with manual expiry for crashed claims — which is exactly what `SKIP LOCKED` gives for free at the SQL layer.

Dispatch failures get exponential backoff via an `attempts` counter; after five failed attempts a row moves to a dead-letter state for manual inspection instead of retrying forever. Because delivery is **at-least-once** — a crash between "the email sent successfully" and "the row got marked processed" causes a replay — every consumer of an outbox message has to be idempotent, or at minimum tolerate an occasional duplicate as a strictly better outcome than the alternative of maybe-never.

## When this pattern actually earns its cost — and when it's overengineering

The bar applied here: **is there a real, lossy side effect, and can the pattern be built in under a day?** Both have to be true. For GroceryEasy: order confirmation emails, SignalR status broadcasts, and low-stock alerts are all things where silently dropping one is a real, demoable failure ("order committed, process crashes, customer never learns their order exists") — and the implementation is genuinely small, ~150 lines plus a polling job, not a multi-week integration project.

The general teaching point: not every side effect deserves this treatment. If losing the side effect occasionally is a non-event (an analytics ping, a cache warm), or if the side effect can be safely and idempotently re-derived from state that's already durable, adding an outbox table, an interceptor, and a background worker is pure overhead — more moving parts, more places to get the retry/backoff logic wrong, and a delivery-guarantee downgrade (at-least-once, pushing idempotency requirements onto every consumer) that isn't free even when it's the right call. Reaching for the outbox pattern reflexively, on every side effect a handler triggers, is itself a design mistake — the pattern is a response to a specific, named failure mode, not a default.

## The scale-up path, out of scope for now

Polling every few seconds means a few seconds of latency between "order placed" and "email dispatched" — fine for this project, but two things get replaced once polling latency or Postgres's single-instance throughput stop being enough:

- **`LISTEN`/`NOTIFY`** (Postgres's built-in pub/sub) lets workers subscribe to a channel and get pushed a notification the moment a row is inserted, instead of polling on a fixed interval — cuts latency to near-instant without adding new infrastructure. It doesn't replace the outbox table itself (`NOTIFY` payloads aren't durable — a listener that's down misses the notification entirely), so in practice it's layered *on top of* the outbox as a low-latency wake-up signal, with the table remaining the durable source of unprocessed work.
- **A message broker (Kafka, RabbitMQ, SQS)** becomes the right answer once there are multiple independent downstream consumers needing separate read positions, cross-service (not just cross-table) event distribution, or throughput beyond what a single Postgres instance's polling/locking can sustain.

Out of scope here because none of those conditions hold: this is a single Postgres instance, a solo project, the side-effect consumers are all in-process within the same monolith, and a Hangfire poll every few seconds is comfortably fast enough for order-confirmation-email latency. Adding Kafka or RabbitMQ operational overhead for this traffic volume would be solving a scaling problem the project doesn't have.

## In GroceryEasy

See `D4` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record — order confirmation emails, SignalR broadcasts, and low-stock alerts are the three side effects that met the bar. Cost is stated plainly there too: ~150 lines plus a polling job, and at-least-once delivery means every handler on the receiving end must be idempotent.

## Interview questions

**Q: Why not just call the email service directly in the order-placement handler?**
Because the process can crash between the order commit and the email call, leaving a committed order the customer was never told about — a real, demoable failure, not a hypothetical. Wrapping it in a try/catch doesn't fix it either, since retry state needs to survive a crash to matter, and durable retry state is what the outbox table *is*.

**Q: Isn't 2-phase commit the textbook-correct way to solve this?**
In theory, yes, if both systems support the XA protocol — but most side-effect targets here (an HTTP email API, SignalR, a webhook) fundamentally can't participate in 2PC regardless of configuration. Even where 2PC is technically available, it adds real latency and a new failure mode (a coordinator crash leaving participants "in doubt," blocked on held locks) that's rarely worth it compared to an outbox.

**Q: What does `FOR UPDATE SKIP LOCKED` actually do, and why does it matter here?**
`FOR UPDATE` locks the rows a query selects for the duration of the transaction; `SKIP LOCKED` tells Postgres to skip any row another transaction already has locked instead of waiting for it. That lets multiple background workers poll the same outbox table concurrently — each claims a different set of rows — without blocking each other or accidentally double-processing the same row.

**Q: What's the actual cost of this pattern — is it free once it's built?**
No — delivery is at-least-once, not exactly-once, so every consumer (the email sender, the SignalR broadcaster) has to tolerate or dedupe an occasional replay. It's also ~150 lines of interceptor and job code that didn't exist before, and one more thing to monitor (the dead-letter state after five failed attempts).

**Q: When would you *not* use this pattern for a side effect?**
When dropping it occasionally is a non-event — an analytics ping, a cache warm — or when the side effect can be safely re-derived later from state that's already durable. The bar this project applies is: is there a real lossy side effect, and can it be built in under a day? If either answer is no, an outbox table is overhead without payoff.
