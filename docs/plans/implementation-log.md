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

*Entries are appended below as work proceeds.*
