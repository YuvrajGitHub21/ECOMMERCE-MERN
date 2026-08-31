> **Status: live working document.** Unlike [`phase-1-brief.md`](phase-1-brief.md), which is the frozen original brief kept verbatim, this file was written **after** inspecting the code that actually exists, and is meant to be edited as work lands. It supersedes `phase-1-brief.md` wherever the two disagree about what is left to do. It does **not** supersede [`docs/engineering-decisions.md`](../engineering-decisions.md) or [`docs/adr/`](../adr/README.md), which remain authoritative on decisions.

# Phase 1 — what is left · agent execution brief

**Scope:** finish the .NET 10 skeleton — persistence, identity, the API layer, the test harness, continuous integration, and the decision records.
**Branch:** `feat/phase-1-api-skeleton` (already checked out; keep using it).
**Blocks:** every later phase.
**Estimated remaining:** roughly 22 of the 36 hours budgeted for Phase 1.

---

## 0. Verified starting state

Everything below was checked against the working tree, not assumed.

### What is already built and working

| Area | State | Evidence |
|---|---|---|
| Solution and seven projects | Done | `backend/GroceryEasy.sln` lists Domain, Application, Infrastructure, Api and three test projects |
| Project reference rules | Done and correct | Application to Domain, Infrastructure to Application, Api to Application and Infrastructure, Domain to nothing |
| Domain primitives | Done | `Entity`, `AggregateRoot`, `IDomainEvent`, `Error`, `ErrorType`, `Result`, `Result<T>`, `ValidationError` |
| Domain unit tests | Done | `Common/EntityTests.cs`, `AggregateRootTests.cs`, `ErrorTests.cs`, `ResultTests.cs` |
| Dispatcher and pipeline | Done | `Abstractions/Messaging/*`, `Messaging/Dispatcher.cs`, `Behaviors/LoggingBehavior.cs`, `ValidationBehavior.cs`, `TransactionBehavior.cs`, `DependencyInjection.cs` |
| Pipeline behaviour tests | Done | `IntegrationTests/Messaging/DispatcherPipelineTests.cs` |
| Central package management | In use | `Directory.Packages.props` holds FluentValidation, Entity Framework Core, Scrutor, Shouldly, xunit.v3 — no `Version` attribute in any project file |
| Build health | Clean | `dotnet build backend/GroceryEasy.sln` gives **0 warnings, 0 errors** |
| Frozen legacy app | Intact | `git diff v1-legacy-node -- legacy-node/` returns empty |

### What is not built

| Task | State | Evidence |
|---|---|---|
| 1.4 Persistence | **Nothing** | `GroceryEasy.Infrastructure.csproj` has zero package references and the project has no `.cs` file at all. No Npgsql, no `EFCore.NamingConventions`, no `ApplicationDbContext`, no migration |
| 1.5 Identity and tokens | **Nothing** | No Identity package, no `ApplicationUser`, no `refresh_tokens` table |
| 1.6 API layer | **Nothing** | `Program.cs` is five lines: `CreateBuilder`, `Build`, `RunAsync`. No endpoints, no Serilog, no OpenAPI, no Scalar, no health checks, no options validation. `appsettings.json` has only `Logging` and `AllowedHosts` — no `Jwt`, `Frontend` or `Email` section |
| 1.7 Architecture tests | **Project exists, zero tests** | `GroceryEasy.ArchitectureTests/` contains only the project file. `NetArchTest.Rules` is not in `Directory.Packages.props` |
| 1.8 Integration harness | **Partly** | The project exists and hosts in-process dispatcher tests, but there is no `Testcontainers.PostgreSql`, no `Respawn`, no `Microsoft.AspNetCore.Mvc.Testing`, no `WebApplicationFactory` |
| 1.9 Continuous integration | **Nothing** | There is no `.github/` directory in the repository |
| 1.10 Decision records | **Nothing** | `docs/adr/` holds 0000, 0001, 0002 and 0013 only. ADR 0003, 0004, 0005, 0006 and 0014 are listed as *Planned* in `docs/adr/README.md` |

### The one thing that is currently broken

`dotnet test` **fails**, and it is not a test failure:

```
$ dotnet test --solution backend/GroceryEasy.sln --no-build
...
GroceryEasy.ArchitectureTests.dll (net10.0|x64) Zero tests ran (1s 187ms)
Exit code: 8
Test run summary: Failed!
  total: 48   failed: 0   succeeded: 48   skipped: 0
```

Forty-eight tests pass. The run still fails because the architecture-test project contains **no tests**, and Microsoft Testing Platform treats "zero tests ran" as exit code 8. Task 1.7 fixes this. Until then, do not add a continuous-integration workflow that runs `dotnet test`, because it will be red on its first run for a reason that has nothing to do with the code.

Two more facts worth carrying:

- The .NET software development kit on this machine is **10.0.204**. `global.json` pins `10.0.200` with `rollForward: latestFeature`, so 10.0.204 is accepted. Docker is 29.4.1.
- In .NET 10, `dotnet test <solution file>` is rejected outright. The correct form is **`dotnet test --solution backend/GroceryEasy.sln`**. Use that everywhere, including in the workflow file.

### Uncommitted work in the tree

`git status` shows the dispatcher, the behaviours, `ValidationError`, the change that un-sealed `Error`, and the pipeline tests as untracked or modified. **Commit these first**, as a single `feat:` commit, before starting Task 1.4. Building on top of an uncommitted foundation makes every later difference unreadable.

---

## 1. Hard constraints — breaking any of these fails the phase

1. **`legacy-node/` is read-only.** No edits, no formatting, no linting, no "while I am in here" fixes. Verified by `git diff v1-legacy-node -- legacy-node/` returning empty.
2. **`TreatWarningsAsErrors` stays on.** No blanket `<NoWarn>`. Narrow, inline, commented suppressions only.
3. **Central package management.** `PackageReference` entries carry no `Version`. Add packages with `dotnet add package <name>` and **no `--version` flag**, so the software development kit resolves a real version, then confirm it landed in `backend/Directory.Packages.props` and strip any `Version` attribute the tool wrote into the project file.
4. **Never invent a package version.** If a package cannot be resolved, stop and report it.
5. **No `Database.Migrate()` at application startup.** Migrations are a separate step, and `/health/ready` must fail while migrations are pending.
6. **Never `DateTime.UtcNow` in Domain or Application.** Inject `TimeProvider`. Slot cutoffs and token expiry in later phases depend on time being fakeable.
7. **Commit messages must not contain a `Co-Authored-By` trailer.** Conventional Commits only: `feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`.
8. **Every architecturally significant decision gets three artifacts**, not one: an entry in `docs/adr/` (immutable, MADR format, consequences split into positive and negative), a section in `docs/engineering-decisions.md`, and a `docs/concepts/*.md` interview primer following [`docs/concepts/TEMPLATE.md`](../concepts/TEMPLATE.md). The concepts folder already holds `result-pattern.md`, `repository-pattern.md`, `minimal-apis.md`, `mediatr.md`, `jwt-refresh-token-strategy.md` and others written ahead of the code — read the relevant one before implementing, and correct it if the implementation diverges.
9. **Work on `feat/phase-1-api-skeleton`.** Merge to `main` by pull request, never by direct push.

### Non-goals — do not build these in Phase 1

Products, categories, variants, inventory, carts, orders, payments, slots (Phases 2 and 4) · multi-tenancy and `store_id` (Phase 2) · object storage and image upload (Phase 2) · any frontend code (Phase 3) · Redis caching, rate limiting, OpenTelemetry, Hangfire (Phases 5 and 6) · a Docker image for the API (Phase 6).

---

## 2. Task breakdown

Run each acceptance check before moving on. Commit at every task boundary.

### Task 1.0 — Commit the work in flight (about 15 minutes)

```bash
git add -A
git commit -m "feat: add CQRS dispatcher, Scrutor pipeline behaviours and ValidationError"
```

**Acceptance:** `git status` clean; `dotnet build backend/GroceryEasy.sln` still reports 0 warnings.

---

### Task 1.4 — Persistence (about 4 hours)

Packages to add to **Infrastructure**: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `EFCore.NamingConventions`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`.

`GroceryEasy.Infrastructure/Persistence/ApplicationDbContext.cs`:

- `ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>` — the generic form is required; the non-generic default keys users by `string`.
- Implements the existing `IApplicationDbContext` from Application. That interface already declares `HasActiveTransaction`, `SaveChangesAsync` and a transaction handle — read it before writing the class, and do not change its shape without saying why.
- `UseNpgsql(...).UseSnakeCaseNamingConvention()` — snake_case comes from `EFCore.NamingConventions`, **not** from Npgsql. Getting this wrong produces PascalCase tables and is easy to miss.
- `ApplyConfigurationsFromAssembly` — one `IEntityTypeConfiguration<T>` per entity in `Persistence/Configurations/`. Never fluent configuration written inline in `OnModelCreating`.

Add an `AuditableEntityInterceptor` stamping `created_at` and `updated_at` from an injected `TimeProvider`.

**The pitfall that costs an afternoon if skipped:** PostgreSQL extensions must be created **by a migration**, not only by `deploy/postgres/init.sql`. That script runs only for the Docker Compose container. Integration tests start a *different* PostgreSQL through Testcontainers with no init script, so extension-dependent schema passes locally and fails in continuous integration. In the first migration:

```csharp
migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext;");
migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
```

Leave `init.sql` alone; both are idempotent.

Schema rules carried from the audit:

- The email column is `citext`, so `Foo@x.com` and `foo@x.com` cannot become two accounts.
- Any phone or pincode column is `varchar`, never numeric. The audit's data-model findings record `pinCode` and `phoneNo` typed as `Number`, which silently destroys leading zeros.
- Every timestamp is `timestamptz` mapped to `DateTimeOffset`.
- Identifiers are `uuid` from `Guid.CreateVersion7()` — already how `Entity` behaves.

**Acceptance:**

```bash
dotnet ef migrations add InitialIdentity -p backend/src/GroceryEasy.Infrastructure -s backend/src/GroceryEasy.Api
docker compose --profile core up -d --wait
dotnet ef database update -p backend/src/GroceryEasy.Infrastructure -s backend/src/GroceryEasy.Api
docker exec groceryeasy-postgres psql -U groceryeasy -d groceryeasy -c "\dt"
```

Tables appear in snake_case, including `asp_net_users` and `refresh_tokens`.

---

### Task 1.5 — Identity and the token layer, the centrepiece of the phase (about 6 hours)

Use ASP.NET Core Identity for credential storage — it brings a vetted password hasher, lockout, security stamps and email-confirmation token providers. Do not hand-roll password hashing `[L-08]`. Build the **token layer** yourself; that is where the engineering signal is. Read [`docs/concepts/jwt-refresh-token-strategy.md`](../concepts/jwt-refresh-token-strategy.md) first.

**The `refresh_tokens` table:**

| Column | Notes |
|---|---|
| `id` | uuid version 7, primary key |
| `user_id` | foreign key, indexed |
| `token_hash` | SHA-256 of the raw token, **unique**. The raw token is never stored |
| `family_id` | uuid, indexed — groups one rotation lineage |
| `expires_at` | timestamptz |
| `created_at` | timestamptz |
| `revoked_at` | timestamptz, null while live |
| `replaced_by_token_id` | uuid, null until rotated |
| `created_by_ip`, `user_agent` | text, for the security log |

**Token rules:**

- Access token: a JSON Web Token, **15 minutes**, carrying claims `sub`, `email`, `role` and `security_stamp`. Returned in the response body; the single-page application holds it in memory only, never in `localStorage` `[L-10]`.
- Refresh token: **opaque 256-bit random** from `RandomNumberGenerator.GetBytes(32)`, base64url encoded. Delivered as a cookie with `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`. Never in the response body. Do **not** set `Path` to `/`, or the cookie is attached to every API call.

**Rotation with reuse detection — implement exactly this, with all five steps inside one transaction:**

```
POST /api/auth/refresh, reading the refresh cookie:

  1. hash the presented token, look it up by token_hash
  2. not found                  -> 401
  3. revoked_at IS NOT NULL     -> REUSE DETECTED:
                                     revoke every token sharing this family_id,
                                     log a security warning with user id and IP address,
                                     return 401
  4. expires_at <= now          -> 401
  5. otherwise                  -> rotate:
                                     revoked_at = now on the presented token,
                                     create a new token in the SAME family_id,
                                     set replaced_by_token_id,
                                     return a new access token and a new refresh cookie
```

Step 3 is the entire point: an already-rotated token being presented means it leaked, so the whole lineage dies.

Validate the **`security_stamp`** claim on every authenticated request, so deleting a user or changing their role invalidates tokens already issued `[L-07]`. In the legacy system a deleted user's token stayed valid and then crashed the request with a null dereference.

**Endpoints:**

| Method | Route | Notes |
|---|---|---|
| POST | `/api/auth/register` | sends the verification email |
| POST | `/api/auth/verify-email` | token plus email |
| POST | `/api/auth/login` | 401 on bad credentials; the response is byte-identical whether or not the email exists |
| POST | `/api/auth/refresh` | reads the cookie, rotates |
| POST | `/api/auth/logout` | **POST, never GET** `[L-10]`; revokes the current family |
| POST | `/api/auth/forgot-password` | **always 200**, whether or not the account exists `[L-11]` |
| POST | `/api/auth/reset-password` | |
| GET | `/api/users/me` | requires authentication |

**Every emailed link is built from a validated `Frontend:BaseUrl` configuration value, never from `Request.Host`** `[L-09]`. In the legacy system that header was attacker-controllable and the reset link pointed at the API instead of the app, so password reset never worked at all.

Send email through **MailKit** to Mailpit on `localhost:1025` in development, behind an `IEmailSender` abstraction with a collecting implementation for tests. Mailpit belongs to the `full` Compose profile, so use `docker compose --profile full up -d` while working on this task.

Seed the roles `Customer`, `StoreStaff`, `StoreManager` and `PlatformAdmin`. The policies that use them land in Phase 2; seeding the roles now avoids an extra migration later.

**Acceptance:** the tests in Task 1.8 pass, the reuse-detection one above all.

---

### Task 1.6 — API layer (about 4 hours)

Minimal APIs with an `IEndpoint` convention. No MVC controllers — see [`docs/concepts/minimal-apis.md`](../concepts/minimal-apis.md) and [`docs/concepts/mvc-controllers.md`](../concepts/mvc-controllers.md).

```csharp
public interface IEndpoint { void MapEndpoint(IEndpointRouteBuilder app); }
```

Register by assembly scan, group with `MapGroup("/api/auth")`, and use `TypedResults` throughout so response types reach the OpenAPI document.

**The error contract is RFC 9457 ProblemDetails, one shape, always.** Write a single `Result` to `IResult` extension mapping `ErrorType` to a status code:

```
Validation -> 400    Unauthorized -> 401   Forbidden -> 403
NotFound   -> 404    Conflict     -> 409   Failure   -> 500
```

Include a stable `type` uniform resource identifier and a machine-readable `errors` member, populated from `ValidationError.Errors` — that type already exists for exactly this purpose. This is the permanent fix for `[L-17]`, where the server returned `{success, error}` while every client read `.message`, so **every error message in the legacy app displayed the word `undefined`**.

Also in this task:

- A global `IExceptionHandler` producing ProblemDetails. Full detail in Development; a generic message plus a correlation identifier in Production.
- `CorrelationIdMiddleware` — read `X-Correlation-Id` or generate one, echo it on the response, and push it into the Serilog log context.
- **Serilog** — console output, compact JSON in Production, `UseSerilogRequestLogging`, enriched with the correlation identifier and the user identifier.
- **OpenAPI** — `AddOpenApi()` and `MapOpenApi()`, built into .NET 10 (Swashbuckle is no longer in the template), with **Scalar** (`Scalar.AspNetCore`, `MapScalarApiReference()`) as the user interface.
- **Health checks** — `/health/live` covering the process only, and `/health/ready` requiring Npgsql reachable **and** `GetPendingMigrationsAsync()` empty. An always-OK health check is what let the legacy app report healthy while completely broken `[L-12]`.
- **Options validated at startup** — bind `Jwt`, `Frontend` and `Email` with `.ValidateDataAnnotations().ValidateOnStart()`. A missing setting must kill the process at boot, not produce an invalid date at first login `[L-12]`. The key names are already fixed by `.env.example`: `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey`, `Jwt__AccessTokenMinutes`, `Jwt__RefreshTokenDays`, `Frontend__BaseUrl`, `Email__Host`, `Email__Port`, `Email__FromAddress`, `Email__FromName`. Match them exactly; do not invent new ones.

**Acceptance:**

```bash
dotnet run --project backend/src/GroceryEasy.Api
```

Scalar loads; `/health/ready` returns 200 against a migrated database and 503 with a migration pending; removing `Jwt__SigningKey` makes the process fail at boot with a readable message.

---

### Task 1.7 — Architecture tests, which also fix the failing test run (about 1.5 hours)

Add `NetArchTest.Rules` to the architecture-test project. Roughly fifty lines, high signal. Read [`docs/concepts/architecture-testing.md`](../concepts/architecture-testing.md) first.

1. `Domain` depends on no other project and has zero package references.
2. `Application` does not reference `Microsoft.EntityFrameworkCore.Design`, Npgsql, or `GroceryEasy.Infrastructure`. Note carefully: Application *does* legitimately reference `Microsoft.EntityFrameworkCore` for `DbSet<T>`, so assert the absence of the **provider**, not of Entity Framework Core itself, or the test contradicts code that already exists and is deliberate.
3. Domain entities have no public property setters.
4. All handlers are `sealed` and `internal`.
5. All `IEndpoint` implementations are `sealed`.

**Acceptance:** all five pass and — the part that matters right now — `dotnet test --solution backend/GroceryEasy.sln` exits **0** instead of 8. Deliberately break one rule locally, confirm it fails, then revert.

---

### Task 1.8 — Integration test harness, the other half of the phase's value (about 4 hours)

Packages: `Testcontainers.PostgreSql`, `Respawn`, `Microsoft.AspNetCore.Mvc.Testing`. Shouldly and xunit.v3 are already present. **Shouldly, not FluentAssertions** — FluentAssertions v8 moved to a commercial licence, the same class of decision as MediatR. Read [`docs/concepts/integration-testing-strategy.md`](../concepts/integration-testing-strategy.md).

Harness shape:

- `IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime` — starts `new PostgreSqlBuilder().WithImage("postgres:17-alpine")`, overrides the connection string, applies migrations once.
- An xUnit **collection fixture** so one container serves the whole run. Do not start a container per test class.
- **Respawn** resets state between tests, configured with `DbAdapter.Postgres` and **`__EFMigrationsHistory` in the ignore list** — forgetting that wipes migration history and every later test fails confusingly.
- `Program.cs` already ends with `public partial class Program;`. Leave it.

**Required tests:**

| Test | Asserts |
|---|---|
| Register, verify, log in | an access token is returned and a refresh cookie is set |
| Log in with an unknown email | 401, and the response is byte-identical to a wrong-password response |
| Register with an existing email | 409, and no second user row |
| Refresh, happy path | new access token, new cookie, and the old token has `revoked_at` set |
| **Refresh reuse detection** | replaying a rotated token gives 401 **and every token in that `family_id` is revoked** |
| Refresh with an expired token | 401 |
| `/api/users/me` unauthenticated | 401 |
| `/api/users/me` with a valid token | 200 and the correct user |
| A deleted user's still-valid token | 401, **not** 500 `[L-07]` |
| Forgot-password with an unknown email | 200, identical to the known-email response `[L-11]` |
| A validation failure | 400 with a populated ProblemDetails `errors` member |

Name the reuse-detection test clearly — it is going in the README as evidence.

**Acceptance:** `dotnet test --solution backend/GroceryEasy.sln` green from a clean clone with only Docker running.

---

### Task 1.9 — Continuous integration (about 2 hours)

Create `.github/workflows/ci.yml`. The directory does not exist yet. Trigger on pull request and on push:

```
- actions/checkout
- actions/setup-dotnet, dotnet-version 10.0.x
- cache NuGet packages
- dotnet restore backend/GroceryEasy.sln
- dotnet format --verify-no-changes         # .editorconfig is enforced, not advisory
- dotnet build backend/GroceryEasy.sln -warnaserror --no-restore
- dotnet test --solution backend/GroceryEasy.sln --no-build
- upload test results
```

Note the `--solution` flag: `dotnet test backend/GroceryEasy.sln` is rejected by this software development kit. Testcontainers needs no extra setup on `ubuntu-latest`; the Docker socket is already present.

For test-result files and coverage, this project runs on Microsoft Testing Platform, not VSTest — `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and `coverlet.collector` are deliberately absent, and the comment in `Directory.Packages.props` says so. Use the Microsoft Testing Platform extension packages, **not** the VSTest `--logger trx --collect` flags the original Phase 1 brief suggested. That instruction in `phase-1-brief.md` is now wrong; this file supersedes it.

**Acceptance:** green on a pull request. Then enable branch protection on `main` requiring this check.

---

### Task 1.10 — Decision records (about 2 hours)

Write these as the decisions are made, not at the end. MADR format, matching the four already in `docs/adr/`. Every one ends with **Consequences split into positive and negative** — a record with no downsides listed reads as marketing.

| Number | Title | Concepts document it pairs with |
|---|---|---|
| 0003 | Vertical slices inside Clean Architecture | `vertical-slice-architecture.md`, `clean-architecture.md` |
| 0004 | Dependency governance — no MediatR, AutoMapper or FluentAssertions | `mediatr.md`, `object-mapping-strategy.md` |
| 0005 | `Result<T>` for expected failures, exceptions for bugs | `result-pattern.md` |
| 0006 | No generic repository — `DbContext` is the unit of work | `repository-pattern.md` |
| 0014 | Minimal APIs with an `IEndpoint` convention over MVC controllers | `minimal-apis.md`, `mvc-controllers.md` |

The concepts documents for all five **already exist**, written before the code. Read each one, and if the implementation diverged from what it describes, fix the document — a study aid that contradicts the code is worse than no study aid.

Move these rows from *Planned* to *Accepted* in `docs/adr/README.md`, and confirm that sections C2, C3, C4, C5 and C6 of `docs/engineering-decisions.md` still match what was built.

**ADR-0004 matters more than it looks.** Three widely used libraries changed licence in 2025; noticing that and designing around it is a governance signal most portfolio projects do not have.

---

## 3. Definition of done

Phase 1 is complete when **all** of these hold, each proved by command output rather than by assertion:

1. `dotnet build backend/GroceryEasy.sln` — 0 warnings, 0 errors.
2. `dotnet test --solution backend/GroceryEasy.sln` — **exit code 0**, with every test project reporting at least one test.
3. The refresh-token reuse-detection test passes: replaying a rotated token revokes the whole family and returns 401.
4. All five architecture tests pass.
5. `dotnet format --verify-no-changes` — clean.
6. Continuous integration green on a pull request, and branch protection on `main` requires it.
7. `/health/ready` returns 503 with a migration pending and 200 once applied.
8. The API fails at boot when a required configuration value is missing.
9. ADRs 0003, 0004, 0005, 0006 and 0014 written; `docs/adr/README.md` updated; the paired concepts documents reconciled with the code.
10. `git diff v1-legacy-node -- legacy-node/` returns empty.

---

## 4. Known pitfalls

- **Extensions in Testcontainers** (Task 1.4) — the single most likely cause of "passes locally, fails in continuous integration".
- **Respawn and `__EFMigrationsHistory`** — not ignoring it wipes migration history between tests.
- **Central package management** — if `dotnet add package` writes a `Version` into a project file, move it to `Directory.Packages.props` and strip the attribute, or the build errors.
- **Snake-case naming** comes from `EFCore.NamingConventions`, not from Npgsql.
- **Identity with `Guid` keys** requires `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`.
- **`TreatWarningsAsErrors` will bite on generated migration files.** Fix them properly rather than disabling the rule globally.
- **Do not scope the refresh cookie to `/`.** Use `Path=/api/auth`.
- **`dotnet test` needs `--solution`** on this software development kit version.
- **A test project with zero tests fails the whole run** with exit code 8. Any new test project must ship with at least one test in the same commit.

---

## 5. Reporting back

When the phase is finished, report:

1. Each definition-of-done item with the **command output** that proves it, not a claim that it passes.
2. Every deviation from this brief and why. Justified deviations are fine; silent ones are not.
3. Anything found that belongs in `docs/legacy-audit.md` or in a later phase brief.
4. The branch name and the commit range.

If something here turns out to be wrong or impossible, **say so and stop** rather than working around it silently.
