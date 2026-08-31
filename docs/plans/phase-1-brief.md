> **Status note:** this is the original agent brief written to scope Phase 1 before it began, kept here verbatim as the working record. It is **not** kept in sync with implementation. For current status and decisions, see [`docs/engineering-decisions.md`](../engineering-decisions.md) and [`docs/adr/`](../adr/README.md). See also [`master-plan.md`](master-plan.md) for the overall project plan this phase is part of.

# GroceryEasy — Phase 1 Agent Brief

**Scope:** .NET 10 skeleton, Clean Architecture, ASP.NET Core Identity with refresh-token rotation, the integration-test harness, and green CI.
**Budget:** ~36 hours. **Blocks:** every subsequent phase.
**Repo:** `f:\MY PROJECT\Ecommerce Mern\ECOMMERCE-MERN`

---

## 0. Context you need before touching anything

GroceryEasy is an online storefront for local Indian kirana (neighbourhood grocery) stores, where the customer chooses **self-pickup or delivery** at checkout. It began as a college-era MERN app. That app is now **frozen** and is being replaced by an ASP.NET Core (.NET 10) API over PostgreSQL, with a Vite/React/TypeScript SPA arriving in Phase 3.

Phase 0 is complete and committed. What already exists:

```
legacy-node/                    FROZEN MERN app — see hard constraints below
docs/legacy-audit.md            20 defects in the old system + how each is designed out
docs/adr/                       ADR 0000, 0001, 0002, 0013 + an index
docker-compose.yml               postgres 17, redis 7, mailpit, minio, aspire dashboard
deploy/postgres/init.sql        creates pg_trgm, unaccent, citext
.env.example                    every setting the API will read
.editorconfig                   C# style rules, enforced by `dotnet format` in CI
global.json                     SDK pinned to 10.0.200, rollForward latestFeature
backend/Directory.Build.props   net10.0, nullable, TreatWarningsAsErrors, analyzers
backend/Directory.Packages.props  central package management, currently EMPTY
README.md                       interim
```

**Read `docs/legacy-audit.md` before writing code.** It is the specification for what this system must make impossible. Several Phase 1 decisions exist specifically to close defects listed there, and they are cross-referenced below as `[L-nn]`.

### Hard constraints — violating any of these fails the phase

1. **`legacy-node/` is read-only.** Do not edit, reformat, lint, fix, or "tidy" anything inside it, no matter how broken it looks. Its defects are documented deliberately. It is tagged `v1-legacy-node` and `docs/legacy-audit.md` links to it.
2. **`TreatWarningsAsErrors` stays on.** Do not disable it, do not add blanket `<NoWarn>` entries to make the build pass. If an analyzer fires, fix the code. The only permitted suppressions are narrow, inline, and commented with the reason.
3. **Central Package Management is in use.** `PackageReference` entries carry **no `Version` attribute**; all versions live in `backend/Directory.Packages.props`.
4. **No `Database.Migrate()` at application startup.** Migrations are applied as a separate step. `/health/ready` must *fail* if migrations are pending.
5. **Do not invent package versions.** Add packages with `dotnet add package <name>` (no `--version`) so the SDK resolves a real one, then confirm it landed in `Directory.Packages.props`.
6. **Commit messages must not contain `Co-Authored-By` trailers.** Use Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `test:`, `refactor:`).
7. **Work on a branch**, not `main`. Suggested: `feat/phase-1-api-skeleton`.

### Explicit non-goals for Phase 1

Do **not** build these. They belong to later phases and building them now will produce rework:

- Products, categories, variants, inventory, carts, orders, payments, slots — **Phase 2 and 4**
- Multi-tenancy / `store_id` scoping and global query filters — **Phase 2**
- Object storage, image upload — **Phase 2**
- Any React or frontend code — **Phase 3**
- Redis caching, rate limiting, OpenTelemetry, Hangfire — **Phase 5/6**
- Docker images for the API — **Phase 6**

Phase 1 delivers **an API that a user can register with, verify, log into, refresh, and log out of** — and the machinery that makes everything after it testable.

---

## 1. Environment check (do this first)

```bash
dotnet --version          # expect 10.0.2xx
docker --version          # expect 29.x
docker compose --profile core up -d --wait   # must exit 0
docker compose --profile core ps             # postgres + redis healthy
```

If `dotnet --version` reports anything below 10, stop and report — `global.json` pins the SDK and the rest of the brief assumes `net10.0`.

---

## 2. Task breakdown

Each task lists its acceptance check. **Run the check before moving on.** Commit at each task boundary.

### Task 1.1 — Solution and project skeleton

```
backend/
  GroceryEasy.sln
  src/
    GroceryEasy.Domain/            classlib
    GroceryEasy.Application/       classlib
    GroceryEasy.Infrastructure/    classlib
    GroceryEasy.Api/               web (empty/minimal API)
  tests/
    GroceryEasy.Domain.UnitTests/  xunit v3
    GroceryEasy.IntegrationTests/  xunit v3
    GroceryEasy.ArchitectureTests/ xunit v3
```

Project references, and **only** these:

```
Application     -> Domain
Infrastructure  -> Application
Api             -> Infrastructure, Application
Domain          -> (nothing)
```

**Three test projects, not four.** A `GroceryEasy.Application.UnitTests` project is deliberately omitted: per the testing strategy, Application handlers are covered by integration tests driven through the real API, because testing a handler with a mocked `DbContext` mostly asserts that the mock was configured correctly. Domain gets dense unit tests because it is pure.

**Acceptance:** `dotnet build backend/GroceryEasy.sln` succeeds with zero warnings. `dotnet sln list` shows 7 projects.

---

### Task 1.2 — Domain primitives

In `GroceryEasy.Domain/Common/`:

```csharp
public enum ErrorType { Failure, Validation, NotFound, Conflict, Forbidden, Unauthorized }

public sealed record Error(string Code, string Description, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);
}
```

`Result` and `Result<T>`:
- `Result.Success()`, `Result.Failure(Error)`, `Result<T>.Success(T)`, `Result<T>.Failure(Error)`
- `IsSuccess` / `IsFailure`, `Error`, and a `Value` that **throws** if accessed on a failure (a bug, not an expected path)
- Implicit conversion from `T` to `Result<T>` for handler ergonomics

Also: `Entity` (equality by `Id`), `AggregateRoot` (holds `IReadOnlyCollection<IDomainEvent> DomainEvents` with `Raise`/`ClearDomainEvents`), `IDomainEvent`.

**All entity IDs are `Guid` generated with `Guid.CreateVersion7()` in the constructor** — sequential, so B-tree inserts stay dense, and non-enumerable, unlike an integer key.

**Design rule:** `Result` is for *expected* failures (email already taken, invalid token). Exceptions are for *bugs and infrastructure* (null argument, DB unreachable, misconfiguration). Never use exceptions for control flow in a handler.

**Acceptance:** `GroceryEasy.Domain.csproj` has **zero** `PackageReference` entries. Unit tests cover `Result` success/failure and that `Value` on a failure throws.

---

### Task 1.3 — CQRS dispatcher (hand-rolled, no MediatR)

MediatR moved to a commercial licence at v13. Do not take that dependency and do not silently pin a stale version. Write the ~150 lines instead.

`GroceryEasy.Application/Abstractions/Messaging/`:

```csharp
public interface ICommand<TResponse>;
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse> where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResponse> where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct);
}

public interface IDispatcher
{
    Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken ct = default);
    Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken ct = default);
}
```

Register handlers by assembly scan with **Scrutor**. Implement pipeline behaviours as a **decorator chain** via `Scrutor.Decorate<>`, applied in this order (outermost first):

1. `LoggingBehavior` — logs handler name, outcome, elapsed ms. Never logs request bodies (they contain passwords).
2. `ValidationBehavior` — runs FluentValidation validators; on failure returns `Result.Failure` with `ErrorType.Validation`. **Does not throw.**
3. `TransactionBehavior` — wraps commands (not queries) in an EF transaction; commits on `IsSuccess`, rolls back otherwise.

**Acceptance:** an integration test asserts a command with an invalid payload returns 400 with a populated `errors` object, and that the handler was never entered.

---

### Task 1.4 — Persistence

Packages: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `EFCore.NamingConventions`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`.

`ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`:

- `optionsBuilder.UseNpgsql(...).UseSnakeCaseNamingConvention()` — this comes from `EFCore.NamingConventions`; Npgsql does **not** provide it.
- `ApplyConfigurationsFromAssembly` — one `IEntityTypeConfiguration<T>` per entity, never fluent config inline in `OnModelCreating`.
- Expose `IApplicationDbContext` from **Application** with only the `DbSet<>`s and `SaveChangesAsync`, so Application never references the EF provider.

**No generic repository.** `DbContext` is the unit of work. Add narrow, aggregate-specific repositories later only where there is real loading logic.

Interceptors:
- `AuditableEntityInterceptor` — stamps `created_at` / `updated_at` on save using injected `TimeProvider`. **Never `DateTime.UtcNow`** anywhere in Domain or Application; the slot and expiry logic in later phases depends on time being injectable.

**⚠ Critical — PostgreSQL extensions must be created by a migration, not only by `deploy/postgres/init.sql`.** That init script runs only for the Docker Compose container. Integration tests use Testcontainers, which starts a *different* Postgres with no init script. If extensions exist only in `init.sql`, tests fail in CI while passing locally. In the first migration:

```csharp
migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS citext;");
migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
```

Keep `init.sql` as-is; both are idempotent.

Schema notes carried from the audit:
- Email column is `citext`, so `Foo@x.com` and `foo@x.com` cannot become two accounts.
- Any phone or pincode column is `varchar`, **never** a numeric type `[L-17]`.
- Every timestamp is `timestamptz` mapped to `DateTimeOffset`.

**Acceptance:** `dotnet ef migrations add InitialIdentity -p src/GroceryEasy.Infrastructure -s src/GroceryEasy.Api` generates. Applying it to a clean database produces the Identity tables plus `refresh_tokens`, in snake_case.

---

### Task 1.5 — Identity and the token layer ★ the centrepiece of this phase

Use **ASP.NET Core Identity** for credential storage — it brings a vetted password hasher, lockout, security stamps, and email-confirmation token providers. Do not hand-roll password hashing `[L-08]`. Build the *token* layer yourself; that is where the engineering signal is.

**`refresh_tokens` table:**

| column | notes |
|---|---|
| `id` | uuid v7, PK |
| `user_id` | FK, indexed |
| `token_hash` | SHA-256 of the raw token, **unique**. The raw token is never stored |
| `family_id` | uuid, indexed — groups a rotation lineage |
| `expires_at` | timestamptz |
| `created_at` | timestamptz |
| `revoked_at` | timestamptz, null while live |
| `replaced_by_token_id` | uuid, null — set when rotated |
| `created_by_ip`, `user_agent` | text, for the security log |

**Token rules:**
- Access token: JWT, **15 minutes**, claims `sub`, `email`, `role`, `security_stamp`. Returned in the response body — the SPA holds it **in memory only**, never `localStorage` `[L-10]`.
- Refresh token: **opaque 256-bit random** (`RandomNumberGenerator.GetBytes(32)`, base64url). Delivered as a cookie: `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`. Never in the response body.

**Rotation with reuse detection — implement exactly this:**

```
POST /api/auth/refresh, reading the refresh cookie:

  1. hash the presented token, look it up by token_hash
  2. not found                  -> 401
  3. revoked_at IS NOT NULL     -> REUSE DETECTED:
                                     revoke every token in the same family_id,
                                     log a security warning with user id + IP,
                                     return 401
  4. expires_at <= now          -> 401
  5. otherwise                  -> rotate:
                                     set revoked_at = now on the presented token,
                                     create a new token in the SAME family_id,
                                     set replaced_by_token_id,
                                     return a new access token + new refresh cookie
```

Step 3 is the whole point: an already-rotated token being presented means it leaked, so the entire lineage dies. Steps 1–5 must run inside one transaction.

Also validate the **`security_stamp`** claim on every authenticated request, so deleting a user or changing their role invalidates already-issued tokens `[L-07]`. In the legacy system a deleted user's token stayed valid and then crashed the request with a null dereference.

**Endpoints:**

| Method | Route | Notes |
|---|---|---|
| POST | `/api/auth/register` | sends verification email |
| POST | `/api/auth/verify-email` | token + email |
| POST | `/api/auth/login` | 401 on bad credentials; identical response whether or not the email exists |
| POST | `/api/auth/refresh` | reads cookie, rotates |
| POST | `/api/auth/logout` | **POST, never GET** `[L-10]` — revokes the current family |
| POST | `/api/auth/forgot-password` | **always 200**, whether or not the account exists `[L-11]` |
| POST | `/api/auth/reset-password` | |
| GET | `/api/users/me` | requires auth |

**Every emailed link is built from a validated `Frontend:BaseUrl` config value. Never from `Request.Host`** `[L-09]` — in the legacy system that was attacker-controllable and the reset link pointed at the API instead of the app, so password reset never worked at all.

Email via **MailKit** to Mailpit (`localhost:1025`) in development. Behind an `IEmailSender` abstraction with a no-op/collecting implementation for tests.

**Acceptance:** the integration tests in Task 1.8 pass, especially the reuse-detection test.

---

### Task 1.6 — API layer

**Minimal APIs with an `IEndpoint` convention.** No MVC controllers.

```csharp
public interface IEndpoint { void MapEndpoint(IEndpointRouteBuilder app); }
```

Auto-register by assembly scan; group with `MapGroup("/api/auth")`; use `TypedResults` throughout so response types flow into the OpenAPI document.

**Error contract — RFC 9457 ProblemDetails, one shape, always.** Write a single `Result -> IResult` extension mapping `ErrorType` to status:

```
Validation -> 400    Unauthorized -> 401   Forbidden -> 403
NotFound   -> 404    Conflict     -> 409   Failure   -> 500
```

Include a stable `type` URI and a machine-readable `errors` member. This is the permanent fix for `[L-12]`, where the server returned `{success, error}` while every client read `.message`, so **every error toast in the legacy app displayed the word `undefined`**.

Also:
- Global `IExceptionHandler` → ProblemDetails. Full detail in Development; generic message plus a correlation id in Production.
- `CorrelationIdMiddleware` — read `X-Correlation-Id` or generate one; echo it on the response; push it into the Serilog `LogContext`.
- **Serilog**: console (compact JSON in Production), `UseSerilogRequestLogging`, enriched with correlation id and user id.
- **OpenAPI**: `AddOpenApi()` / `MapOpenApi()` (built into .NET 10 — Swashbuckle is no longer in the template) with **Scalar** (`Scalar.AspNetCore`, `MapScalarApiReference()`) as the UI.
- **Health checks**: `/health/live` (process only) and `/health/ready` (Npgsql reachable **and** `GetPendingMigrationsAsync()` empty). `init.sql`-style "always returns OK" health checks are what let the legacy app report healthy while completely broken `[L-12]`.
- **Options validation at startup**: bind `Jwt`, `Frontend`, `Email` with `.ValidateDataAnnotations().ValidateOnStart()`. A missing setting must kill the process at boot, not produce an `Invalid Date` at first login `[L-12]`.

**Acceptance:** `dotnet run --project src/GroceryEasy.Api` starts; Scalar UI loads; `/health/ready` returns 200 against a migrated DB and 503 with migrations pending.

---

### Task 1.7 — Architecture tests

`GroceryEasy.ArchitectureTests` with **NetArchTest.Rules**. ~50 lines, high signal:

1. `Domain` depends on no other project and has zero package references.
2. `Application` does not reference `Microsoft.EntityFrameworkCore` or `GroceryEasy.Infrastructure`.
3. Domain entities have no public property setters.
4. All handlers are `sealed` and `internal`.
5. All `IEndpoint` implementations are `sealed`.

**Acceptance:** all five pass. Deliberately break one locally, confirm it fails, revert.

---

### Task 1.8 — Integration test harness ★ the other half of this phase's value

Packages: `xunit.v3`, `Microsoft.NET.Test.Sdk`, `Shouldly`, `Testcontainers.PostgreSql`, `Respawn`, `Microsoft.AspNetCore.Mvc.Testing`.

Use **Shouldly, not FluentAssertions** — FluentAssertions v8 moved to a commercial licence. Same class of decision as MediatR.

Harness shape:
- `IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime` — starts a `PostgreSqlBuilder().WithImage("postgres:17-alpine")` container, overrides the connection string, applies migrations once.
- An `xUnit` **collection fixture** so one container serves the whole run (~20 s startup, then fast). Do not start a container per test class.
- **Respawn** resets between tests. Configure `DbAdapter.Postgres` and **ignore `__EFMigrationsHistory`** — forgetting this wipes the migration history and every later test fails confusingly.
- `Program.cs` needs `public partial class Program;` at the bottom so `WebApplicationFactory` can reference it.

**Required tests for Phase 1:**

| Test | Asserts |
|---|---|
| Register → verify → login | happy path returns an access token and sets a refresh cookie |
| Login with unknown email | 401, and the response is byte-identical to a wrong-password response |
| Register with an existing email | 409, and no second user row |
| Refresh happy path | new access token, new refresh cookie, old token has `revoked_at` set |
| **Refresh reuse detection** | present an already-rotated token → 401 **and every token in that `family_id` is revoked** |
| Refresh with expired token | 401 |
| `/api/users/me` unauthenticated | 401 |
| `/api/users/me` with a valid token | 200 with the right user |
| Deleted user's still-valid JWT | 401, **not** 500 `[L-07]` |
| Forgot-password, unknown email | 200, identical to the known-email response `[L-11]` |
| Validation failure | 400 with a populated ProblemDetails `errors` member |

**Name the reuse-detection test clearly** — it is going in the README as evidence.

**Acceptance:** `dotnet test` green from a clean clone with only Docker running.

---

### Task 1.9 — CI

`.github/workflows/ci.yml`, on pull request and push:

```
- actions/checkout
- actions/setup-dotnet with dotnet-version 10.0.x
- cache NuGet
- dotnet restore
- dotnet format --verify-no-changes        # .editorconfig is enforced, not advisory
- dotnet build -warnaserror --no-restore
- dotnet test --no-build --logger trx --collect:"XPlat Code Coverage"
- upload test results
```

Testcontainers works on `ubuntu-latest` with no extra setup — the Docker socket is present.

**Acceptance:** green on a pull request. Then enable branch protection on `main` requiring this check.

---

### Task 1.10 — ADRs

Write these as you make the decisions, not at the end. MADR format, matching the four already in `docs/adr/`. Every ADR ends with **Consequences split into positive and negative** — one with no downsides listed reads as marketing.

| # | Title |
|---|---|
| 0003 | Vertical slices inside Clean Architecture |
| 0004 | Dependency governance — no MediatR, AutoMapper or FluentAssertions (licensing) |
| 0005 | `Result<T>` for expected failures, exceptions for bugs |
| 0006 | No generic repository — `DbContext` is the unit of work |

Update the table in `docs/adr/README.md`, moving these from Planned to Accepted.

**ADR-0004 matters more than it looks.** Three widely-used libraries changed licence in 2025; noticing that and choosing around it is a governance signal most portfolio projects do not have.

---

## 3. Definition of Done

Phase 1 is complete when **all** of these hold:

1. `dotnet build backend/GroceryEasy.sln` — zero warnings, zero errors.
2. `dotnet test` — all green from a clean clone with only Docker running.
3. The **refresh-token reuse-detection test passes**: presenting a rotated token revokes the whole family and returns 401.
4. All five architecture tests pass.
5. `dotnet format --verify-no-changes` — clean.
6. CI green on a pull request; branch protection on `main` requires it.
7. `/health/ready` returns 503 when migrations are pending, 200 when applied.
8. The API boots with a missing required config value **failing at startup**, not at first request.
9. ADRs 0003–0006 written; `docs/adr/README.md` updated.
10. `legacy-node/` is byte-identical to `v1-legacy-node` — verify with `git diff v1-legacy-node -- legacy-node/` returning **empty**.

---

## 4. Known pitfalls

- **Extensions in Testcontainers.** Covered in Task 1.4, and it is the single most likely cause of "passes locally, fails in CI". Extensions belong in a migration.
- **Respawn and `__EFMigrationsHistory`.** Not ignoring it wipes migration history between tests.
- **`WebApplicationFactory` needs `public partial class Program;`** at the end of `Program.cs`.
- **Central Package Management.** If `dotnet add package` writes a `Version` into the `.csproj`, move it to `Directory.Packages.props` and strip the attribute, or the build errors.
- **Snake-case naming** comes from `EFCore.NamingConventions`, not Npgsql. Easy to miss and produces PascalCase tables.
- **Identity with `Guid` keys** requires `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`; the non-generic default uses `string`.
- **`TreatWarningsAsErrors` will bite on nullable warnings** in generated migration files. Fix them properly rather than suppressing the rule globally.
- **Don't set the refresh cookie `Path` to `/`.** Scope it to `/api/auth` so it is not attached to every API call.
- **`dotnet format` and generated files.** If migrations trip formatting, keep them formatted rather than excluding them.

---

## 5. Reporting back

At the end, report:

1. Which Definition-of-Done items pass, with the command output that proves each — not a claim that they pass.
2. Anything you deviated from in this brief, and why. Deviations are acceptable when justified; silent ones are not.
3. Anything you found that belongs in `docs/legacy-audit.md` or a future phase.
4. The branch name and commit range.

If something in this brief turns out to be wrong or impossible, **say so and stop** rather than working around it silently. This brief was written before the code existed and may be wrong in specifics.
