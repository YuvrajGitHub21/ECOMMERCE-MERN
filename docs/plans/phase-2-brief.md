> **Status: live working document,** written before Phase 2 begins and meant to be edited as work lands. Decisions remain owned by [`docs/engineering-decisions.md`](../engineering-decisions.md) and [`docs/adr/`](../adr/README.md) — if this file and those disagree, those win. See [`master-plan.md`](master-plan.md) §4, §6.3, §6.4 and §6.9 for the reasoning this brief compresses.

# GroceryEasy — Phase 2 agent brief

**Scope:** the catalogue domain, multi-tenancy, object storage for images, PostgreSQL full-text search, admin catalogue management, and the migrator and seeder.
**Budget:** about 36 hours, weeks 4 to 6.
**Branch:** `feat/phase-2-catalog-tenancy`.
**Depends on:** Phase 1 complete — specifically the database context, the dispatcher pipeline, the `IEndpoint` convention, the ProblemDetails contract, and the Testcontainers harness. **Do not start until `dotnet test --solution backend/GroceryEasy.sln` exits 0 on `main`.**

---

## 0. Why this phase is shaped this way

Two things in Phase 2 are cheap now and extremely expensive later.

**Multi-tenancy.** Adding a `store_id` discriminator, global query filters and a stamping interceptor to five tables costs about five evenings. Retrofitting it onto twenty tables that already carry order and payment history costs a month, and every table you forget is a cross-tenant data leak. The plan is explicit that tenancy "lands here or never". Build it in Task 2.2, before the catalogue tables exist, so every table added afterwards inherits it by convention rather than by memory.

**The search column.** `products.search_vector` is a generated `tsvector` column. Adding a generated column to a populated table means a rewrite. Declare it in the same migration that creates `products`.

Everything else in this phase is ordinary create-read-update-delete work with careful schema.

---

## 1. Hard constraints

Everything in the Phase 1 constraint list still applies without change: `legacy-node/` read-only, `TreatWarningsAsErrors` on, central package management with no `Version` in project files, no `Database.Migrate()` at startup, `TimeProvider` instead of `DateTime.UtcNow`, Conventional Commits with no `Co-Authored-By` trailer, work on a branch and merge by pull request. In addition:

1. **Every tenant-owned table carries `store_id` and implements `ITenantEntity`.** No exceptions "just for now". An architecture test enforces it.
2. **Image bytes never touch the API.** Uploads go direct to object storage through a presigned `PUT`. The database stores an `object_key`, never bytes `[L-18]`. This is the structural fix for base64 blobs in documents.
3. **Search queries go through `EF.Functions.WebSearchToTsQuery`,** never string concatenation into SQL and never a regular expression built from user input `[L-13]` `[L-14]`.
4. **`MaxPageSize` is enforced server-side at 50.** A client asking for 999 gets 50, not 999 `[L-18]`.
5. **Ratings are `smallint` with a `CHECK (rating BETWEEN 1 AND 5)` constraint** `[L-19]`, not free text.
6. **Schema is Entity Framework Core migrations only.** The migrator consumes the model; it never defines schema.
7. **Each new architectural decision gets an ADR, a section in `docs/engineering-decisions.md`, and a `docs/concepts/*.md` primer** following [`TEMPLATE.md`](../concepts/TEMPLATE.md). For this phase the concepts documents `multi-tenancy-strategy.md`, `search-strategy.md` and `image-storage-strategy.md` **already exist** — read them first, and correct them if the implementation diverges.

### Non-goals for Phase 2

Do not build: carts, orders, checkout, pricing, reservations, slots or payments (Phase 4 and 5) · any frontend code (Phase 3) · review submission endpoints (the schema lands here, the endpoints need orders, so they land in Phase 5) · Hangfire jobs (Phase 5) · caching (Phase 6) · batch and expiry first-expired-first-out tracking (cut list).

---

## 2. Task breakdown

### Task 2.1 — Store aggregate and the tenancy root (about 3 hours)

`GroceryEasy.Domain/Stores/`:

- `Store` — `slug` (unique), `name`, `timezone` (default `Asia/Kolkata`), `gstin`, `address`, `is_active`.
- `StoreHours` — day of week, open time, close time, one row per day per store.
- `StoreClosure` — a date range with a reason, for holidays.
- `StoreStaff` — the join between a user and a store with a role. This is the table authorization reads from in Task 2.8.

`Store` itself is **not** an `ITenantEntity` — it is the tenant. Everything else in this phase is.

**Acceptance:** a migration creates `stores`, `store_hours`, `store_closures` and `store_staff` in snake_case with the unique index on `slug`.

---

### Task 2.2 — Multi-tenancy, before any catalogue table exists (about 5 hours)

Read [`docs/concepts/multi-tenancy-strategy.md`](../concepts/multi-tenancy-strategy.md) first. The choice is a single database with a `store_id` discriminator; schema-per-tenant and database-per-tenant are rejected and the reasoning belongs in ADR-0010.

Four pieces, and all four are needed for the guarantee to hold:

1. **`ITenantEntity`** — a marker interface in Domain exposing `Guid StoreId { get; }`.
2. **`ICurrentStore`** in Application, resolved by middleware in this order: the route slug `/s/{storeSlug}/...`, then a `store_id` claim for staff, then the user's `default_store_id`.
3. **Global query filters applied by convention** in `OnModelCreating` — loop over every entity type implementing `ITenantEntity` and add the filter, rather than writing the filter per entity. A developer who forgets a `.Where(...)` must still be unable to read another store's data.
4. **A `SaveChanges` interceptor** that stamps `store_id` on insert and **throws** on any attempt to modify it.

**The proof this phase is judged on** is a pair of tests:

- An integration test where store A's manager requests store B's product and receives **404, not 403**. Returning 403 confirms the resource exists, which leaks information across a tenant boundary.
- An architecture test asserting that every `ITenantEntity` in the model has a configured query filter. Without this, the guarantee decays the first time someone adds a table.

**Acceptance:** both tests green, and deliberately removing the filter from one entity makes the architecture test fail.

---

### Task 2.3 — Catalogue domain (about 5 hours)

`GroceryEasy.Domain/Catalog/`:

- `Category` — either store-scoped or platform-global, self-referencing parent for a two-level tree, `slug`, `display_order`.
- `Product` — `name`, `brand`, `description`, `category_id`, `gst_rate`, `hsn_code`, `is_active`, `legacy_mongo_id` (unique, nullable — the migrator's idempotency key), and `search_vector` (Task 2.6).
- `ProductVariant` — **this is what makes the app a grocery app rather than a generic shop.** `sku` (unique per store), `unit` (an enum: Piece, Gram, Kilogram, Millilitre, Litre), `pack_size`, `mrp`, `sale_price`, `is_loose`, `step_qty`, `barcode`, `reorder_level`. Aashirvaad Atta in 1 kilogram, 5 kilogram and 10 kilogram packs is three variants of one product.
- `ProductImage` — `object_key`, `alt_text`, `display_order`, `is_primary`. **Never bytes.**
- `Review` — `rating smallint CHECK (1..5)` `[L-19]`, `unique (product_id, user_id)`, and an `order_id` foreign key that will later enforce verified purchase `[L-05]`. Schema only in this phase; the endpoints need orders and land in Phase 5.

Money is `numeric(12,2)`. Quantity is `numeric(10,3)`, because loose goods are sold in 250-gram steps.

Value objects worth writing now because Phase 4's pricing engine consumes them: `Money` and `StockQuantity`. Keep them pure — Domain has zero package references and an architecture test enforces that.

**Acceptance:** Domain unit tests cover variant invariants — a loose variant must have a `step_qty` greater than zero, `sale_price` must not exceed `mrp`, and a non-loose variant must have a whole-number `pack_size`.

---

### Task 2.4 — Inventory and the stock ledger, schema only (about 2 hours)

- `inventory` — one row per variant per store: `on_hand`, `reserved`, and `xmin` as a concurrency token. Available stock is `on_hand - reserved` and is never stored.
- `stock_ledger` — append-only: `delta`, `reason`, `reference_id`, `occurred_at`. **Stock is never changed without a ledger row.**

The reservation logic itself, the conditional `UPDATE`, and ADR-0007 belong to **Phase 4**. Build the tables and a simple admin adjustment path here so the seeder has somewhere to put stock; do not build reservations yet.

**Acceptance:** an admin stock adjustment writes both the `inventory` update and the matching `stock_ledger` row inside one transaction, proved by an integration test that sums the ledger and compares it with `on_hand`.

---

### Task 2.5 — Object storage and image handling (about 5 hours)

Read [`docs/concepts/image-storage-strategy.md`](../concepts/image-storage-strategy.md).

`IObjectStorage` in Application; the implementation in Infrastructure uses `AWSSDK.S3` with a custom `ServiceURL`. Locally that points at MinIO; in production at Cloudflare R2. Identical API, one code path. The settings already exist in `.env.example` as `Storage__ServiceUrl`, `Storage__AccessKey`, `Storage__SecretKey`, `Storage__Bucket` and `Storage__PublicBaseUrl` — use those exact keys. MinIO is in the `full` Compose profile, so run `docker compose --profile full up -d` for this task.

Flow:

1. Admin requests an upload: the API returns a **presigned `PUT`** for `products/{sha256}.{ext}` plus the object key.
2. The browser uploads directly to storage. Image bytes never pass through the API, so no request-size limit is ever involved.
3. The admin then posts the object key to create the `product_images` row.
4. Renditions at 1200, 600 and 200 pixels in WebP format via `SixLabors.ImageSharp`.

**Content addressing matters.** Keying on the SHA-256 of the bytes is what makes image migration idempotent in Task 2.9 — re-running the migrator cannot produce duplicate objects. Sniff the content type from the file's magic bytes; do not trust the declared type.

**Acceptance:** an integration test against the MinIO container uploads through a presigned URL, then reads the object back. If running MinIO inside the test harness proves awkward, use a local filesystem implementation of `IObjectStorage` for tests and cover the real one with a manually run test — but say so in the report rather than silently skipping it.

---

### Task 2.6 — Search (about 4 hours)

Read [`docs/concepts/search-strategy.md`](../concepts/search-strategy.md). The decision — PostgreSQL full-text search over Elasticsearch — is ADR-0009, and it is one of the two records most likely to make an interviewer sit up, because it is a decision *against* the obvious choice.

- `products.search_vector` is `tsvector GENERATED ALWAYS AS (...) STORED`, weighting name (A) above brand (B) above category (C) above description (D), with a GIN index. Declare it in the migration that creates the table.
- Query with `EF.Functions.WebSearchToTsQuery` — a safe parser, which is what removes the regular-expression and operator injection surface `[L-13]` `[L-14]`.
- Rank with `ts_rank_cd`.
- **Trigram fallback:** when full-text search returns fewer than three hits, fall back to `pg_trgm` similarity, so "amool" finds "Amul" and "magi" finds "Maggi".
- Faceted filters as indexed predicates: category, brand, price range, in-stock only.
- **Keyset pagination**, not offset. One `PagedResult<T>` shape, shared with the frontend by code generation in Phase 3 `[L-18]`.
- Autocomplete debounced at 150 milliseconds and capped at eight results.

**Acceptance:** an integration test posts each of `'; DROP TABLE products; --`, `.*` and `{"$gt":""}` as the query and gets an empty result set rather than an error `[L-13]` `[L-14]`. Capture `EXPLAIN ANALYZE` output showing the GIN index in use and save it for the Phase 6 README.

---

### Task 2.7 — Public catalogue endpoints (about 3 hours)

`GET /api/categories` · `GET /api/products` (search, facets, keyset pagination) · `GET /api/products/{slug}` with its variants and images · `GET /api/stores/{slug}`.

Project inside the Entity Framework query with `.Select(...)` so PostgreSQL returns only the columns used. Never load a whole entity to read two fields — that is the shape of `[L-18]`, and manual projection is also why this project has no AutoMapper.

**Acceptance:** the product list endpoint issues one query per request with no lazy loading, verified by capturing the generated SQL in a test.

---

### Task 2.8 — Admin catalogue management and the authorization sweep (about 4 hours)

Create, read, update and delete for categories, products, variants, images and stock adjustments, all behind `RequireAuthorization("StoreStaff")`, with the store resolved through `ICurrentStore`.

Read [`docs/concepts/authorization-strategy.md`](../concepts/authorization-strategy.md). Authorization is a policy, never an `if` statement in a handler, and never a client-side route guard — the legacy app's admin guard was `isAdmin={true}` hardcoded on an inner route, so all nine admin pages rendered for any logged-in user `[L-06]`.

**The regression test that closes `[L-06]` permanently:** an xUnit `[Theory]` enumerating **every** admin endpoint and asserting 403 for a Customer. Write it as a theory over a list of routes so a newly added admin endpoint that is left unprotected fails the test automatically.

**Acceptance:** the theory covers every admin route and passes; adding an unprotected admin endpoint makes it fail.

---

### Task 2.9 — The migrator and the grocery seeder (about 5 hours)

A new project, `backend/src/GroceryEasy.Migrator` — a `Microsoft.Extensions.Hosting` console application using `MongoDB.Driver` and `Spectre.Console` for progress. Add it to the solution.

```
migrate users|products|orders|images|all  --dry-run --batch-size 200
seed demo --stores 3 --reset
```

**Idempotency:** every migrated row carries `legacy_mongo_id` with a unique partial index; batches run as `INSERT ... ON CONFLICT (legacy_mongo_id) DO UPDATE` inside a transaction. A `migration_runs` table records the last processed identifier so an interrupted run resumes. `--dry-run` runs inside a transaction and rolls back.

**Data corrections applied during migration,** each closing an audit finding: `pinCode` and `phoneNo` from numeric to `varchar` (the audit records both typed as `Number`, which destroys leading zeros); free-string `role` to a validated enum; string `rating` to `int.TryParse` clamped to 1 to 5 `[L-19]`, with unparseable rows written to `migration_rejects` rather than silently dropped.

**Build the migrator, but seed the demo separately.** The legacy catalogue is wrong-domain data — laptops, footwear, cameras. Migrating it produces a grocery app full of cameras. The migrator is a portfolio artifact demonstrated against a dump of the old database; `seed demo` populates three stores and roughly 120 real Indian grocery stock-keeping units across eight categories, with images, stock and store hours.

**Acceptance:** running `seed demo` twice produces the same row counts, not double. Running `migrate all --dry-run` reports what it would do and changes nothing.

---

### Task 2.10 — Decision records (about 1.5 hours)

| Number | Title | Concepts document |
|---|---|---|
| 0009 | PostgreSQL full-text search over Elasticsearch | `search-strategy.md` |
| 0010 | Single-database multi-tenancy with a `store_id` discriminator | `multi-tenancy-strategy.md` |

Object storage over base64 in the database is covered by section F2 of `docs/engineering-decisions.md` and by `image-storage-strategy.md`; add an ADR only if the implementation forced a decision the existing text does not cover. Move 0009 and 0010 from *Planned* to *Accepted* in `docs/adr/README.md`.

---

## 3. Definition of done

1. `dotnet build backend/GroceryEasy.sln` — 0 warnings, 0 errors.
2. `dotnet test --solution backend/GroceryEasy.sln` — exit code 0.
3. `dotnet run --project backend/src/GroceryEasy.Migrator -- seed demo --stores 3 --reset` succeeds, and running it a second time is idempotent.
4. `GET /api/products?q=amool` returns Amul products through the trigram fallback.
5. The store-A-reads-store-B integration test returns **404**.
6. The architecture test asserting a query filter on every `ITenantEntity` passes.
7. The `[Theory]` over every admin endpoint returns 403 for a Customer.
8. The search-injection test returns empty results rather than errors.
9. `EXPLAIN ANALYZE` output showing the GIN index in use is captured in the phase report.
10. ADR-0009 and ADR-0010 written, the index updated, the concepts documents reconciled with the code.
11. Continuous integration green on the pull request.
12. `git diff v1-legacy-node -- legacy-node/` returns empty.

---

## 4. Known pitfalls

- **A generated column cannot be added cheaply to a populated table.** Declare `search_vector` in the migration that creates `products`.
- **Global query filters and `Include`.** A filtered navigation property inside an `Include` behaves differently from what people expect; test a product-with-variants read from the wrong store explicitly.
- **Query filters do not apply to raw SQL.** If any query bypasses LINQ, the filter is not there. Prefer LINQ; if raw SQL is unavoidable, add the `store_id` predicate by hand and comment why.
- **`IgnoreQueryFilters()` is a loaded gun.** Allow it only in the migrator and the seeder, and add an architecture test or code review note if it appears elsewhere.
- **Presigned URL clock skew.** Signed URLs are time-bound; a container with a drifted clock produces confusing 403 responses from storage.
- **MinIO in tests.** Decide early whether integration tests run against a MinIO container or a filesystem implementation, and state the choice in the report.
- **`unaccent` is not immutable**, so it cannot be used directly inside a generated column expression without wrapping. Handle this when declaring `search_vector` rather than discovering it in the migration.

---

## 5. Reporting back

Same protocol as Phase 1: every definition-of-done item proved by command output, every deviation named and justified, anything found that belongs in a later brief, and the branch and commit range. If something here is wrong or impossible, say so and stop rather than working around it silently.
