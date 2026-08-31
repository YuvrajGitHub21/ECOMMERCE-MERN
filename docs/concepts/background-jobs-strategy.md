# Background jobs strategy: Hangfire vs. the alternatives

## The criteria that actually matter for this decision

Not "how do you run code on a schedule" in general — the properties that matter for the specific jobs this project actually needs: expiring stock reservations, dispatching outbox messages, generating fulfilment slots, sending order confirmation emails.

- **A scheduled or queued job must survive a process restart or deploy.** If a job that hasn't fired yet is silently lost when the process recycles, "background job" has quietly become "best-effort, if nothing redeploys in the meantime" — unacceptable for work like expiring a stock reservation, where losing the job means inventory stays held (and unsellable) indefinitely.
- **Transient failures need automatic retry with backoff, not a single attempt.** A payment webhook call, an email send, or a database blip can fail transiently; the job needs to try again on a growing delay, not fail once and vanish.
- **Operators need visibility into what's queued, running, succeeded, or failed.** A background job system with no way to answer "did last night's reservation-expiry sweep actually run" is undebuggable in production.
- **The job store shouldn't require a new piece of infrastructure to operate.** This project already runs Postgres for everything else; a background-job system that needs its own database, its own broker, or its own ops story is a cost that should be justified, not assumed.

## Comparison at a glance

| Criterion | Hangfire + Postgres (chosen) | Bare `IHostedService`/`BackgroundService` | Quartz.NET |
|---|---|---|---|
| Persistence | Built in — jobs stored in the same Postgres instance | None — state lives in memory only | Opt-in (`AdoJobStore`); in-memory (`RAMJobStore`) by default |
| Survives restart/deploy | Yes | No — an unfired scheduled job is just gone | Yes, only if a persistent job store is explicitly configured |
| Retry with exponential backoff | Built in, automatic | Must be hand-written per job | Must be configured (misfire policies); less automatic |
| Dashboard / visibility | First-party web dashboard, ships with the package | None — `ILogger` output or nothing | No first-party dashboard |
| Extra infrastructure required | None — reuses the app's existing Postgres | None | None, if `AdoJobStore` targets the same database |
| Scheduling primitives (cron, calendars, misfire policies) | Cron expressions, recurring jobs | Fully manual | Rich — calendars, misfire policies, trigger groups |
| Familiarity in .NET shops | Widely run, de facto standard | N/A | Present, but less common than Hangfire |

## Why not each alternative — the technical case

### A bare `IHostedService`/`BackgroundService` with a manual timer loop

The obvious "you don't need a library for this" option, and the one that looks simplest until a restart happens.

- **No persistence.** A `PeriodicTimer` loop's state — what's scheduled, what's in flight — lives entirely in the process's memory. A deploy, a crash, or a container recycle mid-interval means whatever was due to run in that window simply never runs, and there is no record anywhere that it was ever supposed to. For a job like reservation-expiry, that's not a cosmetic gap: inventory held by an expired reservation that never got released stays unsellable until something else notices.
- **No retry-with-backoff for free.** Exponential backoff, jitter, a retry cap, and recording that a job gave up after N attempts is real code that has to be written and tested per job — Hangfire ships this once, already tested, for every job registered with it.
- **No visibility beyond logs.** Answering "what's queued right now" or "did the low-stock alert job run last night, and did it succeed" means grepping application logs, not looking at a page. That's a real operational cost the first time something needs debugging in production.
- **Where it's still the right tool:** a single lightweight recurring task with no persistence requirement, where "it didn't run this one time" is a genuine non-event — an in-memory cache warm-up on startup, say. GroceryEasy still uses bare `BackgroundService` for exactly that category of work; it isn't banned, it's wrong specifically for anything whose failure to run is a real business problem.

### Quartz.NET

The comparably mature, genuinely real alternative — not a strawman. Quartz predates Hangfire as the standard .NET scheduling library, and its trigger model is, on pure scheduling expressiveness, richer than Hangfire's.

- **The concrete difference is what ships turned on by default.** Quartz's default job store, `RAMJobStore`, is in-memory — the same "gone on restart" problem as a bare hosted service, unless persistence is explicitly opted into via `AdoJobStore`, which requires provisioning Quartz's own schema and maintaining that migration path separately from the application's. Hangfire persists from the start; there's no in-memory mode to accidentally ship with in production.
- **No first-party dashboard.** Quartz exposes APIs to query job and trigger state, but there's no bundled web UI — building or adopting one is separate work. Hangfire's dashboard ships in the package and is live the moment the middleware is registered.
- **The philosophical difference, stated plainly:** Quartz is more of a pure *scheduler* — precise cron expressions, calendars with holiday exclusions, misfire policies for what to do when a trigger was missed — while Hangfire is more of a persisted, retryable *work queue* with scheduling as one feature of it. Quartz is the better tool when the actual requirement is complex calendar-driven timing ("run at 2 AM on weekdays, except these five holidays, in this specific time zone"). GroceryEasy's actual jobs are closer to "do this unit of work, retry until it succeeds, and don't lose it if the process dies mid-way" — which is Hangfire's default mode, not something bolted onto its scheduler.
- **Where it would win:** a system whose core need really is rich, calendar-aware scheduling rather than persisted retryable execution — a batch reporting pipeline with holiday-aware cutoffs, for instance. Not this project's shape of background work.

## Chosen: Hangfire with PostgreSQL as its job store

Hangfire persists every job — fire-and-forget, delayed, recurring — to the same Postgres instance the rest of the application already runs against, in its own schema. Concretely, this buys:

- **Jobs survive restarts because they're in the database, not in memory.** A reservation-expiry job scheduled five minutes from now is a row; a deploy in the meantime doesn't lose it, because nothing about it depended on the process staying alive.
- **Automatic retry with exponential backoff**, built in and configured per job attribute rather than hand-rolled — a transient failure (a flaky outbound email send, a momentary DB blip) is retried on a growing delay before it's given up on and recorded as failed.
- **An operationally useful dashboard**, live out of the box: queued, processing, succeeded, and failed jobs, with the ability to inspect a failure's exception and requeue it manually. This is the direct answer to "how do you know a background job actually ran" — a screenshot, not a log-grepping exercise.
- **No new infrastructure.** The job store lives in the database that's already running; there's no separate broker, no Redis-as-queue, no second service to keep alive.

## The non-technical factor — stated separately

Hangfire is also, concretely, what a large share of .NET shops actually run for this kind of work — it is shared vocabulary in an interview in a way a hand-rolled scheduler or a less common library isn't. That's a real factor in the choice, but it's an audience-fit reason, not an engineering one: the technical case above (persistence, retry, dashboard, no new infrastructure) stands on its own regardless of who's reading the code afterward.

## In GroceryEasy

See `F3` in [`../engineering-decisions.md`](../engineering-decisions.md) and [ADR-0012](../adr/0012-hangfire-over-hosted-services.md) for the formal record. Hangfire runs the reservation-expiry sweep (releasing stock held by an abandoned checkout — see `D2` in engineering-decisions.md), the outbox dispatch job (`D4`, claiming batches with `FOR UPDATE SKIP LOCKED` and a dead-letter after five attempts), and fulfilment slot generation. **Cost, stated honestly:** Hangfire owns its own schema in the shared database, and its dashboard is a real endpoint that must be secured behind authorization before it's exposed — an unsecured job dashboard is an operational data leak in its own right.

## Interview questions

**Q: Why not just a `BackgroundService` with a timer — isn't that simpler for a project this size?**
Simpler until the first restart. A timer loop's schedule lives entirely in process memory, so a job due to fire during a deploy or crash is silently lost with no record it was ever supposed to run. For something like expiring a stock reservation, that's not cosmetic — it's inventory that stays held and unsellable indefinitely. Hangfire persists the job itself, so a restart delays it, it doesn't erase it.

**Q: Quartz.NET is the classic .NET scheduler — why Hangfire over Quartz?**
Quartz's default job store is in-memory (`RAMJobStore`) — persistence is opt-in via `AdoJobStore`, which means provisioning and maintaining Quartz's own schema separately. Hangfire persists by default and reuses the application's own database. Quartz also has no first-party dashboard. The deeper difference is philosophy: Quartz is a richer pure scheduler (calendars, misfire policies); Hangfire is a persisted, retryable work queue with scheduling built in. This project's jobs are "retry until it succeeds, don't lose it on restart" — Hangfire's default shape, not Quartz's.

**Q: Doesn't persisting every job to the primary database add load to it?**
Some, yes — it's a real, stated trade-off. The counter-argument is that it's a known, bounded cost (job rows in Hangfire's own schema) against the alternative of standing up and operating a separate broker or job database for what, at this project's scale, is a modest job volume. If job throughput ever became large enough to contend with transactional traffic, that's the point to reconsider — not before.

**Q: Walk me through what happens if a background job fails.**
Hangfire catches the exception, records it against that job's history (visible in the dashboard with the full stack trace), and schedules a retry on an increasing delay per its configured retry policy. After the configured attempt count is exhausted, the job is marked failed rather than silently dropped — an operator can see it in the dashboard and requeue it manually if the underlying issue is fixed.

**Q: The Hangfire dashboard is a real web page — how do you keep that from being a security hole?**
It's mounted behind an authorization filter requiring the same staff/admin policy as the rest of the admin surface — it is not exposed unauthenticated. An unsecured job dashboard would leak operational detail (job payloads, retry counts, failure reasons) to anyone who found the URL, so securing it isn't optional polish, it's part of choosing to run a dashboard at all.
