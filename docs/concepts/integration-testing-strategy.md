# Integration testing strategy: Testcontainers vs. the alternatives

## The criteria that actually matter for this decision

This project's entire technical thesis is that correctness under concurrency and constraints is provable, not assumed (`D2`, `D3`, `D5` in [`engineering-decisions.md`](../engineering-decisions.md)). A test suite that can't actually exercise those mechanisms doesn't just under-test — it produces false confidence, which is worse than no test at all. The criteria that matter for **this specific test problem**, not integration testing in general:

- **The test has to run real SQL against the real engine.** A conditional atomic `UPDATE`, a `CHECK` constraint, a unique-violation-as-idempotency-check, and `xmin`-based optimistic concurrency are all Postgres-specific behaviors. Anything that doesn't run actual PostgreSQL can't prove any of them work.
- **Isolation between test runs.** Twenty concurrent-order tests, an idempotency test, and a tenancy-isolation test all mutate shared state; if two runs (or two developers, or CI and a laptop) share a database, they interfere with each other in ways that produce flaky, hard-to-reproduce failures.
- **Must run unattended in CI**, from a clean checkout, with no pre-existing infrastructure to provision by hand.
- **Cost has to be proportional to what's being proven.** ~20 seconds of container startup is a real cost per test run; it has to buy something a cheaper alternative genuinely can't.

## Comparison at a glance

| Criterion | EF Core in-memory provider | SQLite in-memory | Shared dev/CI database | Chosen: Testcontainers |
|---|---|---|---|---|
| Runs real SQL | No — a fake `IQueryable` store | Yes, SQLite's dialect | Yes, real Postgres | Yes, real Postgres |
| Enforces `CHECK` constraints | No | Partially, weaker typing model | Yes | Yes |
| Real transactions | No | Limited (file-level locking) | Yes | Yes |
| Postgres-specific behavior (`xmin`, conditional `UPDATE`) | Can't exist | Can't exist — no `xmin` equivalent | Yes | Yes |
| Isolated per test run | Trivially (in-process) | Trivially (in-process) | No — shared state | Yes — fresh container |
| Runs unattended in CI | Yes | Yes | No — needs provisioned infra | Yes |
| Startup cost | None | None | None (already running) | ~20s per collection |

## Why not each alternative — the technical case

### EF Core's in-memory provider

- It isn't a database — it's an in-memory `IQueryable` store that mimics EF Core's change-tracking surface. It doesn't parse or execute SQL, doesn't enforce `CHECK` constraints, doesn't support real ACID transactions, and has no concept of row versioning.
- Concretely, for this project: `rating smallint CHECK (rating BETWEEN 1 AND 5)` can't be violated in-memory, because there's no constraint being enforced — the test would pass whether or not the constraint exists in the actual migration. The conditional `UPDATE ... WHERE on_hand - reserved >= @qty` that makes inventory reservation safe under concurrency (`D2`) has no in-memory equivalent at all — there's no concurrent-writer semantics to test.
- This is the sharpest failure mode of the option: **a passing test proves nothing about whether the real behavior works.** For a project whose whole point is proving correctness under concurrency and constraints, that's not a minor gap — it's testing the wrong thing while looking like it tests the right thing, which is worse than having no test, because it creates false confidence that ships.
- **Where it's fine:** true unit tests of pure logic that never touches persistence — which is exactly why `Domain` has zero package references (`C1`) and is tested with plain xUnit facts, no database involved at all. The in-memory provider's actual niche is testing that a LINQ query *compiles and shapes data correctly*, not that the database enforces anything.

### SQLite in-memory mode

The most credible alternative, and the one that actually deserves a real technical rebuttal rather than a dismissal — SQLite in `:memory:` mode is commonly recommended, runs real SQL, and does enforce some real constraints.

- It's a genuinely different SQL dialect and a genuinely different concurrency model from PostgreSQL, and the gap is exactly the part of this project that matters most. SQLite has no `xmin` system column, no row-level MVCC in Postgres's sense, and a fundamentally different locking model (single-writer, file/database-level locking vs. Postgres's per-row MVCC with genuine concurrent writers). The optimistic-concurrency pattern used for admin edits (`D2`) is built on `xmin` — there is no SQLite equivalent to test against.
- The conditional `UPDATE ... WHERE on_hand - reserved >= @qty` pattern that makes inventory reservation safe (`D2`) is standard SQL and *will run* against SQLite — but the property being proven (that Postgres serializes concurrent writers on that row so "zero rows affected" is the correct, race-free out-of-stock signal) depends on Postgres's specific MVCC and locking behavior under real concurrent connections. A test passing against SQLite's single-writer model doesn't exercise concurrent writers the way Postgres does, so a test suite green against SQLite can still hide a genuine race condition that only appears against Postgres's actual concurrency behavior.
- Postgres-specific SQL used elsewhere in this project — `tsvector`/`GIN` full-text search (`F1`), `pg_trgm` similarity — has no SQLite equivalent at all; those tests simply couldn't run.
- **Where it would win:** a project whose production database genuinely *is* SQLite (embedded apps, some serverless/edge patterns), or a codebase deliberately kept database-agnostic where the ORM abstraction is the actual contract under test, not the specific engine's behavior. Neither is true here — this project is deliberately, permanently Postgres (`A3`), so testing against a different engine's semantics doesn't validate the thing that will actually run in production.

### A shared, persistent developer/CI database

- Tests interfere with each other: two tests asserting on the same inventory row, run concurrently (as the 20-concurrent-order test in `D2` requires by design), corrupt each other's expected before/after state unless carefully partitioned — and partitioning shared state correctly across an entire suite is its own ongoing maintenance burden.
- Can't be provisioned fresh for an isolated CI run. A clean CI job needs to start from a known, empty state; a shared database accumulates whatever the last run (or the last developer) left behind, so "did this test fail because of a real bug, or because of leftover data from an earlier run" becomes a routine debugging question instead of a structurally impossible one.
- Doesn't compose with a "clone the repo and run the tests" workflow — it requires out-of-band infrastructure (connection strings, credentials, network access) that doesn't exist from a clean checkout, which directly works against the project's own Phase 6 "done" criterion: a stranger clones the repo and it works.
- **Where it would win:** none, really, for automated test isolation — it's a pattern that persists mainly out of infrastructure inertia (someone already stood up a shared staging database) rather than because it's the right tool for this job.

## The chosen approach: Testcontainers + Respawn

- **Testcontainers** starts a real `postgres:17-alpine` container per test run (scoped via an xUnit collection fixture — see [xunit-vs-nunit.md](xunit-vs-nunit.md) for why xUnit's per-test-instance model pairs naturally with this), so every CI run and every developer's machine gets the exact engine version production runs, with real transactions, real constraints, and real MVCC concurrency behavior.
- **Respawn** resets table data between individual tests within that one container — faster than tearing down and recreating the container per test, while still guaranteeing one test's leftover rows never leak into the next test's assertions.
- The cost is real and worth stating plainly: roughly 20 seconds of container startup per test run, and Docker becomes a hard requirement for running the test suite at all — a genuine tax on local dev loop speed, paid once per run rather than once per test.

### The environment-parity trap this approach almost fell into

`deploy/postgres/init.sql` creates the Postgres extensions (`pg_trgm`, etc.) the application depends on — but only for the **Docker Compose** container used for local development. Testcontainers spins up a **different**, fresh Postgres container with no init script at all, so a test suite relying on those extensions existing would pass locally (against the Compose container, which has run `init.sql`) and fail mysteriously in CI (against the Testcontainers instance, which hasn't).

The fix is structural rather than a reminder to keep two init scripts in sync: **extensions are created inside an EF Core migration itself** (a raw-SQL migration step running `CREATE EXTENSION IF NOT EXISTS pg_trgm`), so the same migration pipeline that creates every table also provisions every extension a table's constraints or indexes depend on — no separate, easily-forgotten init script for either environment.

This generalizes beyond extensions: **any assumption about what's already present in "the database" that lives outside the migration history is an environment-parity bug waiting to happen** — it will be true wherever the assumption was made valid by hand (a local machine, a long-lived staging database, a Compose container someone provisioned once) and false everywhere a truly fresh database is created, which is exactly what CI, a new developer's clone, and Testcontainers each do. Anything the application needs to run — extensions, seed reference data, roles — belongs in a migration or an explicitly-run seeding step, never in a one-time script assumed to have already happened.

## The non-technical factor — stated separately, if one exists

None beyond what's already covered in [language-platform-choice.md](language-platform-choice.md). The 20-second startup cost is a real, stated trade-off, but the decision itself is driven entirely by whether a test can prove the thing it claims to prove — not by cost or convenience.

## In GroceryEasy

See `G1` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record. This is the harness the 20-concurrent-orders test (`D2`), the idempotency-key uniqueness test (`D3`), and the tenancy-isolation 404 test ([multi-tenancy-strategy.md](multi-tenancy-strategy.md)) all run on — none of the three would prove anything real against the in-memory provider or SQLite. Paired with xUnit's per-test-instance isolation model; see [xunit-vs-nunit.md](xunit-vs-nunit.md) for why that pairing is deliberate rather than incidental.

## Interview questions

**Q: Why not the EF Core in-memory provider — isn't it built exactly for this?**
It's built for testing that a LINQ query compiles and shapes data correctly, not for testing database behavior — it doesn't run SQL, doesn't enforce `CHECK` constraints, and has no transaction semantics. This project's central correctness claims are about a conditional atomic `UPDATE` under concurrency and a `CHECK` constraint on rating values — neither can be exercised by a fake in-memory store. A green suite against it would prove nothing about whether those mechanisms actually work, which is worse than no test, because it looks like coverage.

**Q: SQLite in-memory mode runs real SQL and is a very common recommendation — why not that?**
It's the closest real alternative, and I'd call out the honest gap directly: SQLite's concurrency model is fundamentally different from Postgres's. It has no `xmin` system column, so the optimistic-concurrency pattern used for admin edits can't be tested at all, and its single-writer locking model doesn't exercise the same concurrent-writer behavior that makes Postgres's conditional `UPDATE` race-free. A suite green against SQLite could still hide a real race condition that only shows up under Postgres's actual MVCC behavior — which is precisely the thing this project needs proven.

**Q: What's the actual cost of Testcontainers, and is it worth it?**
About 20 seconds of container startup per test run, and Docker becomes a hard local requirement — a real tax on the dev loop. It's worth it because the alternative is a test suite that can't prove the specific Postgres behaviors — conditional atomic updates, `xmin` concurrency, real constraint enforcement — that are this project's actual point. Respawn resetting data between individual tests inside that one container keeps the per-test cost close to zero; the 20 seconds is paid once per collection, not once per test.

**Q: What's the extensions trap you ran into, and what's the general lesson?**
`deploy/postgres/init.sql` creates Postgres extensions for the local Docker Compose container, but Testcontainers starts a completely fresh container with no init script — so a test relying on an extension existing would pass locally and fail in CI with no obvious reason why. The fix is creating extensions inside an EF Core migration instead of a separate init script, so the same pipeline that provisions every table also provisions everything those tables depend on. The general lesson is that any assumption about what's "already there" in a database — outside the migration history — is an environment-parity bug waiting for the first genuinely fresh environment, whether that's CI, Testcontainers, or a new developer's clone.

**Q: A shared staging database is what a lot of teams actually use for integration tests — why is that wrong here?**
It fails on isolation and reproducibility, not correctness of the SQL itself: the 20-concurrent-orders test in particular depends on starting from a known, empty state, and a shared database accumulates state from every prior run and every other developer. A failure becomes ambiguous — real bug, or leftover data — in exactly the place where this project needs failures to be unambiguous. It also can't be spun up fresh in an isolated CI job without external provisioning, which works against the "clone and run" experience the project is designed around.
