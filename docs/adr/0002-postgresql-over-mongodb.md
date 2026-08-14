# 0002 — PostgreSQL over MongoDB

- **Status:** Accepted
- **Date:** 2026-08-15
- **Related:** [0001](0001-clean-room-rewrite-over-strangler-fig.md), [`docs/legacy-audit.md`](../legacy-audit.md)

## Context

The legacy system stored everything in MongoDB via Mongoose: three collections (User, Product, Order), with reviews embedded on products, shipping addresses embedded on orders, and the cart kept in browser `localStorage`.

Several of the [audited defects](../legacy-audit.md) trace directly to that choice, or to what it failed to prevent:

- **L-04** — stock decrement was a read-modify-write with no atomicity, and could drive stock negative. Nothing in the schema forbade a negative quantity.
- **L-19** — a review's rating was stored as whatever type arrived, so a string rating produced a 27-star average. No constraint existed to reject it.
- **L-15** — the cart lived in `localStorage`, so it had no server-side existence and could not participate in a transaction with order creation.
- **L-14** — user input flowed into `.find()` as query operators.
- Data model — no indexes anywhere; `enum`-less status and role fields; `pinCode` and `phoneNo` typed as `Number`, silently destroying leading zeros.

The new system needs guarantees the old one had no way to express: order creation, inventory reservation, slot booking and the outbox write must either all happen or none of them happen.

## Options considered

### A. Stay on MongoDB, using the official C# driver

Cheapest path — no data migration, and the document shapes carry over. MongoDB does support multi-document transactions on a replica set.

Rejected because it keeps the burden of correctness in application code. Constraints like "rating is between 1 and 5", "stock is never negative", or "one review per user per product" would remain conventions enforced by whoever remembers, rather than by the database. It also forgoes EF Core, migrations and relational modelling — the things a C# team will actually expect fluency in.

### B. PostgreSQL with EF Core

### C. PostgreSQL for transactional data, MongoDB retained for the catalogue

Rejected as unjustifiable complexity. Two databases, two consistency models and a sync problem, for a catalogue of a few thousand rows that Postgres serves comfortably.

## Decision

**Option B.** PostgreSQL 17 as the single system of record, accessed through EF Core with Npgsql.

The domain is relational and always was. Orders reference variants, variants reference products, products belong to stores, reservations reference both an order and a variant. Modelling that as documents means either duplicating data or hand-rolling joins in application code — which is what the legacy app did, and why an order's line items silently drifted from the products they referenced.

Specifically, this buys:

- **Real transactions.** Reserving inventory, booking a slot, creating the order and writing the outbox row all commit or all roll back. This is the foundation the entire correctness argument rests on.
- **Constraints the application cannot bypass** — `CHECK (rating BETWEEN 1 AND 5)`, `unique (product_id, user_id)`, `unique (cart_id, variant_id)`, foreign keys. An entire class of defect stops being representable rather than merely being tested for.
- **Atomic conditional updates.** `UPDATE inventory SET reserved = reserved + n WHERE on_hand - reserved >= n` makes "no rows affected" *mean* out-of-stock, with no read-then-write window. This is the direct structural answer to L-04.
- **Versioned, reviewable schema migrations** via EF Core, applied at release rather than at startup, with the generated SQL reviewable as a build artifact.
- **Full-text search built in** — a generated `tsvector` column with a GIN index, plus `pg_trgm` for typo tolerance. Removes the regex-injection surface of L-13 and avoids adding a search engine ([ADR-0009](0009-postgres-fts-over-elasticsearch.md), pending).
- **`numeric(12,2)` for money**, with no floating-point representation error, and `numeric(10,3)` for quantities so loose goods can be sold in 250 g increments.
- **`xmin` as a free optimistic-concurrency token**, needing no extra column.

## Consequences

**Positive**
- The invariants that matter are enforced by the database, so they hold regardless of which code path writes.
- EF Core migrations make schema change reviewable and reversible, replacing "whatever shape the app happened to write last".
- Query plans are inspectable with `EXPLAIN ANALYZE`, so performance work is measurement rather than guesswork.
- Relational modelling, EF Core and migrations are directly transferable to the work this project is meant to demonstrate readiness for.

**Negative**
- Schema changes now require a migration. Adding a field is minutes rather than seconds, and expand/contract discipline is mandatory for anything destructive.
- A one-time migration tool must be built and made idempotent ([`GroceryEasy.Migrator`](../../backend/src/GroceryEasy.Migrator)), including converting base64 image blobs into object storage.
- The base64-image-in-document pattern is gone. That is an improvement, but it forces object storage (R2 in production, MinIO locally) into the stack in Phase 2 rather than later.
- Free-tier managed Postgres is more constrained than free-tier Mongo Atlas, which influenced the hosting choice.
- Embedded documents were genuinely convenient for reviews and shipping addresses. Those become joins, which is more correct and slightly more code.
