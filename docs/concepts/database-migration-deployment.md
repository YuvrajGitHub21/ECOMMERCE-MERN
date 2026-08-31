# Database migration deployment: apply-at-release vs. the alternatives

## The criteria that actually matter for this decision

Schema migrations are the one deploy step that can permanently destroy data if it goes wrong, and the one most tutorials get away with getting wrong because a tutorial only ever runs one instance. The criteria that matter for **how a migration actually reaches a running database**, not migrations in general:

- **Exactly-once application.** A migration must run once per deploy, not once per running instance. Anything that runs it as a side effect of application startup breaks this the moment there's more than one instance.
- **Blast radius of the credential that's allowed to run DDL.** The credential the running API uses for every normal request is a fundamentally different risk than the credential used once, briefly, during a deploy step.
- **A safety net for the failure case**, not just a plan for the success case. Deploys fail. What happens when the schema and the code disagree needs to be a designed answer, not an emergent one.
- **Reviewability before irreversible things happen.** A `DROP COLUMN` or a `RENAME` is destructive and often not practically reversible once data has flowed through the new shape. Someone (or some process) should be able to look at the exact SQL before it runs against production data.
- **Zero-downtime compatibility.** The old code and the new code both have to run against *some* version of the schema during a rolling deploy — the migration and the deploy are not one atomic instant, they're a window.

## Comparison at a glance

| Criterion | `Database.Migrate()` at startup | Manual DBA-run process | Chosen: CI-generated script, applied at release |
|---|---|---|---|
| Exactly-once guarantee | No — every instance races to apply it | Yes, but a human is the mechanism | Yes — one deploy step, before rollout |
| Runtime DB user needs DDL | Yes, permanently | No | No — zero DDL permission at runtime |
| Reviewable before running | No — buried in app startup logs | Yes, but often informally | Yes — a committed, diffable build artifact |
| Fits a CI/CD pipeline | Technically yes, badly | No — a human bottleneck | Yes — designed for it |
| Rollback story | None — it already ran on every pod that started | Whatever the DBA's runbook says | Health-check gate blocks rollout on mismatch |
| Speed / automation | Fast, zero ceremony | Slow, ticket-driven | Fast, but scripted rather than implicit |

## Why not each alternative — the technical case

### `Database.Migrate()` in `Program.cs` at application startup

The default a tutorial reaches for, and the most credible "why not just do the easy thing" question.

- **Multiple instances race.** The moment there's more than one replica — which any real deploy eventually has, even a blue/green rollout briefly running two — every instance calls `Migrate()` on startup. EF Core's migration history table gives some protection via row locking, but it's a database lock being used to paper over an architectural problem: N processes independently deciding "I should be the one to change the schema" is fragile at exactly the moment (deploy time) when it matters most, and different EF Core/Npgsql versions have shipped different degrees of protection against concurrent migration races — relying on it is relying on an implementation detail, not a designed guarantee.
- **The runtime database user needs DDL permission, permanently.** To call `Migrate()`, the connection string the *running API* uses for every ordinary `SELECT`/`UPDATE` must also be able to `ALTER TABLE`, `DROP COLUMN`, `CREATE INDEX`. That means a SQL injection, a compromised dependency, or a bug that builds a raw query from user input has a blast radius that includes the schema itself, not just the data in it. The principle of least privilege says the request-handling credential should be able to do exactly what a request needs — CRUD on rows — and nothing else.
- **No point to intervene.** If the migration is wrong, it has already run, on whichever pod started first, against production data, before anyone had a chance to look at it. There's no artifact to review beforehand and no gate to stop it.
- **Where it's genuinely fine:** a single-instance side project, a local dev loop, or a demo you tear down and recreate — contexts where "only one process will ever call this" is actually true and the DB user is never exposed to untrusted input. It is not a safe default to reach for once there's more than one instance or a production security boundary to respect, which is exactly the gap between "worked in the tutorial" and "worked in production."

### A manual, DBA-run migration process

The traditional enterprise answer, and a real, still-common alternative — not a strawman.

- **Safer per-migration, by construction.** A human reviews the SQL, runs it in a maintenance window, and can stop between statements if something looks wrong. For a high-stakes, low-frequency schema change (a multi-terabyte table, a data-loss-risk operation), this is genuinely the right call even today.
- **Doesn't fit a CI/CD pipeline.** The entire point of continuous delivery is that a change can go from commit to production without a human being the bottleneck at each step. Inserting "a DBA runs this by hand" turns every deploy that touches the schema into a scheduling problem — a ticket, a maintenance window, someone awake at the right time — which defeats the purpose of automating everything else about the pipeline.
- **Doesn't scale with deploy frequency.** This project (and most modern SaaS) deploys far more often than a DBA-gated process assumes is normal. At even a few deploys a week, "wait for a human to run the migration" becomes the slowest step in the pipeline by a wide margin.
- **Where it would win:** a regulated environment where every schema change legally requires a named human sign-off, or an operation genuinely risky enough (irreversible, multi-hour, touching a huge table) that automating it away would remove a safety check that's earning its keep.

## The chosen approach: CI-generated script, applied before rollout, no runtime DDL

- **CI generates the SQL migration as a build artifact**, using EF Core's `migrations script` generation (idempotent — safe to run against a database at any prior migration state, not just the immediately preceding one). It's a real file, diffable in the build output, not something buried in application logs.
- **Deploy applies that script as a discrete step, before the new application image rolls out.** The sequence is: build → generate migration script → apply script against the target database → then, and only then, deploy the new API image. Schema and code are never applied as a single atomic event; the schema change happens first, deliberately, using its own credential.
- **The runtime database user has no DDL permission at all.** The connection string the API uses in production can `SELECT`/`INSERT`/`UPDATE`/`DELETE` on application tables and nothing more — it structurally cannot `ALTER TABLE` even if every other defense fails. A different, higher-privilege credential — used only by the deploy pipeline, only for the duration of that one step — applies the script.
- **`/health/ready` fails when migrations are pending.** The readiness probe checks the database's actual applied-migrations state against what the running binary expects. If a deploy step is skipped, or a rollback leaves code and schema mismatched, the new instance fails its readiness check instead of serving requests against a schema it doesn't understand — the orchestrator keeps routing traffic to the last-known-good instance rather than a half-working one. This is the direct fix for `L-12`: the legacy app's health check returned `"Hello World"` without touching the database at all, so a completely broken deploy — including a swallowed Mongo connection failure — was reported healthy.

### Expand/contract: why no migration is ever a single destructive step

The other half of zero-downtime deploys is that the *schema change itself* has to tolerate old code and new code running against it simultaneously during a rolling deploy — there's always a window where some instances are on the old binary and some are on the new one, both hitting the same database.

A migration that does `ALTER TABLE orders RENAME COLUMN total TO total_amount` in one step breaks the old instances immediately: they're still issuing `SELECT total FROM orders` and get a SQL error the moment the rename lands, before every old instance has finished draining. The fix is to split what looks like one conceptual change into deploys that are each independently safe:

1. **Expand.** Add the new column, nullable, alongside the old one (`total_amount`, nullable, next to `total`). Both old and new code can run unmodified — old code never sees the new column, new code hasn't shipped yet.
2. **Migrate/backfill.** Deploy code that writes to both columns, and backfill existing rows from old → new. Old and new columns now agree.
3. **Cut over.** Deploy code that reads and writes only the new column.
4. **Contract.** Once every instance is confirmed on the new code (no rollback risk), a later deploy drops the old column.

Each of the four steps is independently deployable and independently safe against a rolling restart — at no point does a mix of old and new instances hitting the same schema produce an error. A rename or a `NOT NULL` added directly, in one migration, collapses these four safe steps into one unsafe one — it can only ever be correct if every instance switches over atomically, which a rolling deploy by definition does not guarantee.

## The non-technical factor — stated separately, if one exists

None beyond what's already covered in [language-platform-choice.md](language-platform-choice.md). The choice here is driven entirely by exactly-once semantics, least privilege, and zero-downtime compatibility — not cost or audience fit.

## In GroceryEasy

See `F6` in [`docs/engineering-decisions.md`](../engineering-decisions.md) and [ADR-0011](../adr/0011-migrations-at-release.md) for the formal record. This is a Phase 6 concern (`migrations-at-release`), landing alongside Dockerfiles and image publishing. It closes the same class of gap as `L-12` — a health check that lies about the system's real state — one layer down, at the schema rather than the connection.

## Interview questions

**Q: Why not just call `Database.Migrate()` in `Program.cs` — every tutorial does it that way?**
It works when exactly one process will ever call it, which is true in a tutorial and false the moment there's more than one running instance. Multiple instances starting up race to apply the same migration, and the runtime database user needs DDL permission permanently just to make that call possible — meaning every request-handling credential in production can `ALTER TABLE`, not just `SELECT`/`UPDATE`. Applying the migration as a discrete pre-deploy step means the runtime user needs no DDL permission at all, and it only ever runs once, deliberately, not once per pod.

**Q: A DBA manually running migrations is a real, common pattern — why not that?**
It's the safer answer per individual migration — a human reviews the SQL and can stop mid-change if something looks wrong — and it's the right call for a genuinely high-stakes, low-frequency schema change even in this project. But it doesn't fit a CI/CD pipeline: it turns every schema-touching deploy into a scheduling problem gated on a specific person's availability, which defeats the point of automating the rest of the pipeline. The chosen approach keeps the reviewability (the script is a committed artifact, diffable before it runs) without the human bottleneck on every deploy.

**Q: What actually stops a bad migration from taking down production?**
Two independent things. First, the script is generated and applied as its own pipeline step before the new image rolls out, so it's reviewable and isolated from application deploy risk. Second, `/health/ready` checks the database's actual migration state against what the running binary expects — if they disagree for any reason, the instance fails its readiness probe and the orchestrator keeps traffic on the last known-good version instead of routing to a broken one.

**Q: What's an expand/contract migration, concretely?**
Splitting a schema change that looks like one edit into several independently-safe deploys: add a new nullable column, deploy code that writes to both old and new, backfill existing rows, cut reads over to the new column, and only in a later deploy drop the old one. Each step is safe against a rolling deploy where old and new code briefly run side by side against the same schema. A direct rename or an added `NOT NULL` in one step isn't safe under that condition — it assumes every instance switches atomically, which a rolling deploy never guarantees.

**Q: Doesn't a build-artifact SQL script duplicate what EF Core's migration classes already give you?**
No — it's generated *from* the same migration classes EF Core already has, via `migrations script`, so there's no duplicated authoring. What changes is when and how it's applied: instead of being executed implicitly by application code with a high-privilege connection string, it becomes an explicit, reviewable file that a separate, narrowly-scoped deploy step runs once.
