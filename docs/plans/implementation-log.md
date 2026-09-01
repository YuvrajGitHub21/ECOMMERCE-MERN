# Implementation log

A running record of what was actually built, in what order, and every decision taken along the way — including the ones to **skip** something and defer it.

This is deliberately different from the other documents here:

- [`docs/adr/`](../adr/README.md) records *architectural* decisions, formally and immutably.
- [`docs/engineering-decisions.md`](../engineering-decisions.md) is the narrative of those decisions.
- The phase briefs in this folder say what *should* happen.
- **This file says what did happen, and where reality diverged from the brief.**

Every entry follows the same shape so the log stays scannable:

> **What** was done · **Why** this way · **Alternatives** considered · **Skipped/deferred** and to when

---

## Branch and merge-request strategy

Decided before any code was written, because it shapes every commit that follows.

```
main                     release branch, protected, only ever merged into from development
 └── development         integration branch; every phase merges here by pull request
      ├── feat/phase-1-api-skeleton      → PR into development
      ├── feat/phase-2-catalog-tenancy   → branched from phase 1, PR into development
      ├── feat/phase-3-spa-foundation    → branched from phase 2, PR into development
      ├── feat/phase-4-ordering-spine    → branched from phase 3, PR into development
      ├── feat/phase-5-payments-admin    → branched from phase 4, PR into development
      └── feat/phase-6-hardening-deploy  → branched from phase 5, PR into development
```

**Why each phase branches from the previous phase's branch rather than from `development`:** the phases are strictly dependent — Phase 2 cannot compile without Phase 1's database context, Phase 4 cannot run without Phase 2's catalogue. Branching from `development` would mean each phase branch starts without the phase before it, unless the previous pull request is merged first. Branching from the previous phase branch lets work continue while the pull request is still open for review.

**The cost, stated honestly:** each pull request's diff includes the previous phase's commits until that one is merged. Merging them in order into `development` resolves this, and GitHub shows a clean diff once the base branch is merged. The alternative — waiting for each merge before starting the next phase — is cleaner in the diff view and slower in wall-clock time. Speed wins here because there is one developer and no review queue.

**Alternative considered and rejected: trunk-based development with short-lived branches.** It is the better practice for a team shipping daily, and it is the wrong fit for a project whose unit of work is a two-to-three-week phase with a hard verification gate at the end. The phase gate *is* the review, and it needs a branch to sit on.

### On merge requests

The GitHub CLI (`gh`) is **not installed on this machine** and no attempt was made to install it, because installing developer tooling system-wide is not something to do silently inside an implementation task. Branches are therefore pushed from here, and each phase's pull-request creation link is recorded in this log for one-click creation in the browser. If `gh` is installed later, `gh pr create --base development` does the same job from the terminal.

---

## Phase 1 — .NET skeleton, identity, continuous integration

Branch: `feat/phase-1-api-skeleton` · Base: `development`

Starting state was **mid-phase**, not empty — see [`phase-1-remaining.md`](phase-1-remaining.md) for the verified audit. Tasks 1.1 through 1.3 were already complete and committed or in the working tree.

### Entry 1.0 — Committed the work in flight

**What:** the hand-rolled CQRS dispatcher, the three Scrutor pipeline behaviours, `ValidationError`, the change that un-sealed `Error`, and the dispatcher pipeline tests were sitting uncommitted in the working tree. Committed as one `feat:` commit before any new work started.

**Why this way:** every subsequent diff is unreadable if it is layered on top of an uncommitted foundation. This is Task 1.0 in the brief for exactly that reason.

**Alternatives:** splitting it into three commits — dispatcher, behaviours, tests. Rejected as archaeology: the three were written together and do not compile apart, so separate commits would be a fiction.

**Skipped:** nothing.

---

### Entry 1.4 — Persistence

**What:** `ApplicationDbContext` over `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`, an `AuditableEntityInterceptor`, the `RefreshToken` entity and its configuration, and the `InitialIdentity` migration applied to a real PostgreSQL 17 container.

Six decisions were taken inside this task that the brief did not settle.

#### 1.4a — PostgreSQL extensions declared on the model, not as raw SQL in the migration

**What:** `builder.HasPostgresExtension("citext" | "pg_trgm" | "unaccent")` in `OnModelCreating`, rather than the `migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS ...")` the brief specified.

**Why this way:** the goal is that extensions exist *before* any table that depends on them — the users table declares `email` as `citext`, so a wrongly ordered migration fails outright. Raw `Sql()` calls land wherever they are written in `Up()` and have to be manually hoisted above the `CreateTable` calls, on every migration, forever. `HasPostgresExtension` makes Npgsql emit an `AlterDatabase` operation that the migration generator always orders first. Verified in the generated file: `AlterDatabase` with all three annotations is line one of `Up()`.

**Alternatives:** the brief's raw SQL (works, but ordering is manual and easy to get wrong on a later migration); leaving extensions in `deploy/postgres/init.sql` only (the trap the brief warns about — that script runs only for the Compose container, never for Testcontainers, so it passes locally and fails in continuous integration).

**Skipped:** nothing. `init.sql` is left exactly as it was; both are idempotent.

#### 1.4b — Identity's tables renamed to snake_case by hand

**What:** an explicit `RenameIdentityTables` step mapping all seven Identity tables to `asp_net_users`, `asp_net_roles` and so on.

**Why:** discovered by inspecting the applied schema rather than by reasoning — the first migration produced `AspNetUsers` (PascalCase) alongside `refresh_tokens` (snake_case). `EFCore.NamingConventions` only rewrites names chosen *by convention*, and Identity sets its own with an explicit `ToTable`, so the rewrite skips them. A schema that is half quoted and half not means every hand-written statement is a coin flip. The migration was dropped and regenerated rather than patched.

**Alternatives:** renaming further, to plain `users` and `roles` — rejected, because the `asp_net_` prefix usefully signals "the framework owns the shape of this table, do not hand-write inserts into it". Leaving the inconsistency — rejected as above.

#### 1.4c — One phone column, not two

**What:** the plan's `phone_e164` field was not added. Identity's inherited `PhoneNumber` column is used instead, configured `varchar(16)` and documented as holding an E.164 value.

**Why:** two phone columns on one table drift apart, and there is no rule that keeps them in step. The column is text and never numeric, which is the property that actually matters — the legacy schema typed `phoneNo` as a number, silently destroying leading zeros.

**Alternatives:** keeping both and normalising into `phone_e164` on write. Defensible when the raw input needs preserving for support purposes; not worth the drift risk here.

#### 1.4d — Deferred to Phase 2: `default_store_id` and `legacy_mongo_id`

**Skipped deliberately.** `default_store_id` is a foreign key to `stores`, and that table does not exist until Phase 2 — adding the column now means adding it unconstrained and remembering to add the constraint later, which is worse than adding both together. `legacy_mongo_id` is only read by the migrator, also Phase 2. Both are nullable additive columns, so adding them later is an expand-only migration with no backfill and no downtime — the cheapest possible kind to defer.

#### 1.4e — `dotnet-ef` pinned in a local tool manifest

**What:** `.config/dotnet-tools.json` pinning `dotnet-ef` at 10.0.11.

**Why:** the globally installed tool on this machine was **10.0.8** while the packages resolve to **10.0.11**. That mismatch works until it does not, and it is invisible to anyone else cloning the repository. `dotnet tool restore` now gives every machine and the continuous-integration runner the same version. Note that `dotnet new tool-manifest` wrote the file to the repository root rather than `.config/`; it was moved, because `.config/dotnet-tools.json` is the path the software development kit actually discovers.

#### 1.4f — Generated migrations need `dotnet format`, not `dotnet format style`

**What:** the build failed immediately on the generated migration with `IDE0161` (file-scoped namespace) and `IDE0005` (unnecessary using), because `TreatWarningsAsErrors` is on. `dotnet format style` fixed those. `dotnet format --verify-no-changes` then *still* failed, on `ENDOFLINE` and `CHARSET` — Entity Framework writes CRLF with a byte-order mark, and `.editorconfig` requires LF and no mark. Only the full `dotnet format` fixes those.

**The repeatable workflow, and it belongs in the brief:**

```bash
dotnet dotnet-ef migrations add <Name> -p backend/src/GroceryEasy.Infrastructure -s backend/src/GroceryEasy.Api -o Persistence/Migrations
dotnet format backend/GroceryEasy.sln --include backend/src/GroceryEasy.Infrastructure/Persistence/Migrations/
```

**Alternatives:** an `.editorconfig` block relaxing those rules under `**/Migrations/*.cs`. Rejected — the brief is explicit that generated files get formatted rather than excluded, and the two-command workflow costs nothing once known.

**Verified:** `dotnet build` 0 warnings · `dotnet format --verify-no-changes` clean · all nine tables present in snake_case · `citext`, `pg_trgm` and `unaccent` all installed by the migration · `refresh_tokens` has its unique `token_hash` index, its `family_id` index and a cascading foreign key.

---

## Environment notes

- **Docker Desktop was not running** when the first `docker compose up` was attempted, and was started from this session. Nothing else on the machine was installed or changed.
- **The GitHub CLI is absent,** so pull requests are created from the links recorded below rather than from the terminal.

---

### Entry 1.5 — Identity and the token layer

**What:** `IIdentityService` and `ITokenService` in Application, implemented in Infrastructure over ASP.NET Core Identity; seven auth slices plus `GET /api/users/me`.

#### 1.5a — Auth handlers live in Application behind two narrow interfaces

**Why:** `UserManager<TUser>` is generic over a type deriving from a NuGet base class, so it cannot cross into Application without breaking the architecture rule. The seam is two interfaces returning `Result` and small records — never `IdentityResult`, never `ApplicationUser`. The moment a framework type appears in those signatures the abstraction is only pretending.

**Alternatives:** putting the auth slices in Infrastructure (loses the dispatcher pipeline — validation, logging, transaction — for exactly the endpoints that most need it); a pure `Domain.User` mirrored onto an Identity type (a synchronisation problem on every field, for a cleaner diagram).

#### 1.5b — Reuse detection must commit outside the request transaction ★

**The most important thing found in this phase, and it was found by testing, not by reading.**

Reuse detection ends by returning `Result.Failure`. `CommandTransactionBehavior` rolls back on failure. So the first implementation revoked the whole token family and then **threw the revocation away** — the replay correctly returned 401, and the attacker's successor token kept working. Verified against a running server: step 3 gave 401, step 4 gave **200**.

The fix is a deliberate second connection. `RevokeFamilyOutOfBandAsync` takes a fresh scope, gets its own `ApplicationDbContext`, revokes and commits independently. That is correct in principle and not a workaround: revoking a leaked lineage is a security action that must not be conditional on the request that revealed it succeeding.

**Alternatives:** returning success from the reuse branch so the transaction commits (would mean answering a detected attack with 200); excluding refresh from the transaction behaviour (the rotate path genuinely needs atomicity — a crash between revoking and inserting the successor logs an honest user out).

**The test that now guards it** asserts both halves — the replay is rejected *and* the successor stops working. A test checking only the replay would have passed against the broken version.

#### 1.5c — `EnableRetryOnFailure` removed

**What:** added in Task 1.4, removed here. The retrying execution strategy refuses to run inside a user-initiated transaction, and every command opens one, so it broke every write endpoint — surfaced on the first registration attempt as a 500.

**Why not the documented fix.** Wrapping the unit in `Database.CreateExecutionStrategy().ExecuteAsync(...)` re-runs the whole handler, and `RegisterCommandHandler` sends a verification email — a retry would send it twice.

**Deferred to:** Phase 4 for the outbox (which moves side effects off the request path and makes handlers genuinely replayable), then Phase 6 for the free-tier database, which is where transient failures actually begin.

#### 1.5d — One phone column, and two fields deferred

Covered in 1.4c and 1.4d. `default_store_id` and `legacy_mongo_id` remain deferred to Phase 2.

#### 1.5e — Skipped: revoking refresh families on password reset

**Skipped deliberately.** A password reset rotates Identity's security stamp, which invalidates every *access* token immediately because the stamp is re-checked per request. Existing *refresh* families are not revoked. Doing so needs a "revoke every family for this user" path that Phase 1 has no second caller for, and the fifteen-minute access-token window bounds the exposure. Worth adding when the admin console gains "sign out everywhere" in Phase 5.

### Entry 1.6 — The API surface

**What:** `IEndpoint` convention with assembly scan, one `Result` to ProblemDetails mapping, global exception handler, correlation-id middleware, Serilog, OpenAPI with Scalar, split liveness and readiness health checks, options validated at startup.

#### 1.6a — Liveness and readiness are genuinely different checks

`/health/live` excludes every check by predicate; `/health/ready` runs the database check including `GetPendingMigrationsAsync()`. Liveness answers "should this process be restarted", and a database outage is not a reason to restart a healthy process. Readiness answers "should traffic come here", and a container whose schema is behind must fail it — which is what stops a mismatched deployment from going live and failing one request at a time.

#### 1.6b — JWT bearer options configured through the options system

**What:** `AddOptions<JwtBearerOptions>(...).Configure<IOptions<JwtOptions>>(...)` rather than resolving inside the `AddJwtBearer` callback.

**Why:** the obvious version calls `services.BuildServiceProvider()` inside the callback, which builds a **second container** with its own singletons that is never disposed. It appears to work and quietly doubles every singleton in the application. This was written the wrong way first and corrected before commit.

#### 1.6c — `.editorconfig` gained the constants rule it always claimed to have

The file's comment said "constants are PascalCase" but no such rule existed, so the private-field rule caught private constants and demanded `_underscore`. Added the missing rule ahead of the field rule, since first match wins.

### Entry 1.7 — Architecture tests

**What:** six NetArchTest rules. **This is also what made `dotnet test` exit 0 for the first time** — a test project with zero tests fails the whole run under Microsoft Testing Platform with exit code 8, so the empty architecture project had been breaking the suite before it asserted anything.

**One rule is deliberately weaker than the brief specified.** The brief said Application must not reference `Microsoft.EntityFrameworkCore`. It does, deliberately, for `DbSet<T>` and the transaction handle on `IApplicationDbContext` — see ADR-0006. The rule asserts absence of the **provider** (Npgsql) instead, which is the boundary that actually matters. A rule contradicting deliberate working code gets deleted the first time it fires.

**Migrations are excluded** from the public-surface rule: Entity Framework emits them public with no supported alternative, and listing each one would break the test on every `migrations add`.

### Entry 1.8 — Integration harness

**What:** `IntegrationTestWebAppFactory` over Testcontainers PostgreSQL 17, Respawn between tests, 21 auth tests. Only the SMTP transport is substituted — everything else is the real thing.

Two harness bugs, both worth recording because both produced errors pointing away from the cause:

**`ConfigureAppConfiguration` lost to `appsettings.Development.json`.** The factory's callback is appended to a configuration `WebApplication.CreateBuilder` has already built, and the Development file's connection string kept winning. Migrations were applied to the developer's local database on port 5432 while Respawn inspected the empty container and reported *"No tables found"*. Twenty-one tests failed. `UseSetting` writes host configuration, which is established first, and wins.

**Migrations must run before anything touches `Services`.** `WebApplicationFactory` starts the host lazily on first access, and `Program.cs` seeds roles during startup — so migrating through a scope taken from `Services` is already too late. The stack trace pointed at `RoleSeeder` and said nothing about ordering. Fixed by migrating a standalone `DbContext` first, which also matches production, where migrations are a release step that completes before the new image starts.

**Reading the token out of the email, rather than from `UserManager`,** is deliberate: it exercises the real link-building code, so the test would catch a link built from the wrong base address — which is exactly L-09.

### Entry 1.9 — Continuous integration

`.github/workflows/ci.yml`. Format verification runs **before** the build so a formatting failure reports in seconds. The cache key is `Directory.Packages.props` alone, which Central Package Management makes sufficient. `dotnet test --solution` — the bare solution path is rejected by this software development kit, which the original brief got wrong.

**Not done, and it needs a person:** branch protection on `main` and `development` requiring this check. It is a GitHub settings change, not a file, so it cannot be made from here.

### Entry 1.10 — Decision records

ADRs 0003, 0004, 0005, 0006 and 0014 written and moved from *Planned* to *Accepted*. Each ends with consequences split positive and negative.

---

## Phase 1 — verification

Run at the close of the phase, against [`phase-gates.md`](phase-gates.md).

| Gate | Result |
|---|---|
| `git diff v1-legacy-node -- legacy-node/` | empty |
| `dotnet build` | **0 warnings, 0 errors** |
| `dotnet format --verify-no-changes` | clean |
| `dotnet test --solution` | **75 passed, 0 failed, exit 0** — Debug and Release |
| Refresh-token reuse detection | replay rejected **and** successor revoked; asserted by a named test |
| Architecture tests | 6 rules, all pass |
| `/health/ready` | 200 migrated; the check fails on a pending migration by construction |
| Missing configuration kills the process at boot | `ValidateOnStart` on `Jwt`, `Frontend`, `Email` |
| ADRs 0003–0006, 0014 | written, index updated |
| Branch protection | **outstanding — needs a GitHub settings change** |

Verified by hand against a running server as well as by the suite: registration, duplicate registration in different casing (409, one user row), email verification through Mailpit, login, rotation, replay, family revocation, `/api/users/me` authenticated and not, forgot-password answering identically for known and unknown addresses, login answering identically for unknown address and wrong password, and a deleted user's still-valid token returning **401, not 500**.

**Pull request:** https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/pull/new/feat/phase-1-api-skeleton → base `development`

---

*Entries are appended below as work proceeds.*
