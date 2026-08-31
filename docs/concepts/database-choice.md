# Database choice: PostgreSQL vs. the alternatives

## The criteria that actually matter for this workload

GroceryEasy's domain — orders, inventory, payments, multi-tenant stores — is relational by nature, not by accident: an order references variants, variants reference products, products belong to stores, and reservations reference both an order and a variant. The criteria that matter for **this specific shape of data**, not database selection in general:

- **Real multi-statement transactions, not single-document atomicity.** Placing an order means reserving inventory, booking a fulfilment slot, creating the order, and writing an outbox row — all four, or none. That's inherently a multi-record operation.
- **Constraints the application cannot bypass.** "A rating is 1–5," "stock never goes negative," "one review per user per product" need to be true regardless of which code path writes, not true only if every caller remembers to check.
- **Atomic conditional writes for a hot-contention resource.** Inventory decrement under concurrent checkout is a read-then-write race if the database can't express "update this row only if a condition still holds" as one operation.
- **A genuinely relational query shape** — joins across orders, line items, variants, products, and stores, with filtering, faceting, and pagination — rather than data that's naturally a tree of nested documents.
- **Exact decimal arithmetic for money and quantities.** Floating-point representation error in a price or a loose-goods quantity (sold in 250 g increments) is a correctness bug, not a rounding nuisance.

## Comparison at a glance

| Criterion | PostgreSQL | MongoDB | MySQL/MariaDB | DynamoDB / key-value NoSQL |
|---|---|---|---|---|
| Multi-statement transactions | Native, full ACID | Supported since 4.0 (replica set required), historically bolted on | Native, full ACID (InnoDB) | Limited (`TransactWriteItems`, 100-item/4MB cap, single-region) |
| Schema-enforced constraints | `CHECK`, FK, `UNIQUE`, all native | None — enforced by app code or optional JSON Schema validators | `CHECK`, FK, `UNIQUE`, all native | None — item shape is whatever was written |
| Atomic conditional update | `UPDATE … WHERE condition` | `findOneAndUpdate` with a filter — works, but not enforced by schema | `UPDATE … WHERE condition` | `ConditionExpression` on `PutItem`/`UpdateItem` — works, per-item only |
| JSON/semi-structured column support | `jsonb` — indexed, queryable, binary-stored | Native (it's the storage model) | `JSON` type — text-stored, weaker indexing than `jsonb` | Native (schemaless) |
| Full-text search | Built in (`tsvector` + GIN, `pg_trgm`) | Built in, less capable than dedicated engines | Built in (`MATCH … AGAINST`), weaker ranking than Postgres FTS | None (needs a separate service) |
| Exact decimal type | `numeric(p,s)`, no float error | Uses `Decimal128` — correct, but every schema/field must opt in manually | `DECIMAL(p,s)`, no float error | No native decimal — apps typically store as string or scaled integer |
| Standards compliance / query portability | High — ANSI SQL, broadly transferable | N/A — proprietary query language | High — ANSI SQL, some MySQL-specific extensions | N/A — proprietary API, least portable of the four |
| Fit for a relational, transactional domain | Native fit | Requires modelling around it | Native fit | Requires modelling around it |

## Why not each alternative — the technical case

### MongoDB (staying on the existing document model)

The cheapest path on paper — no data migration, document shapes carry over from the legacy app — and the most direct comparison, since it's what's being replaced.

- Correctness becomes a matter of *convention*, not *enforcement*. "Rating is between 1 and 5" or "stock is never negative" would remain rules that live in application code, true only if every write path remembers to check them. The legacy audit is the concrete evidence this fails in practice: `L-19` — a rating stored as whatever type arrived, producing a 27-star average because nothing rejected a non-numeric value — and `L-04` — stock going negative because nothing at the schema level forbade it.
- Modelling a genuinely relational domain (orders → variants → products → stores) as documents means either duplicating data across documents or hand-rolling joins in application code. The legacy app did the latter, which is why order line items could silently drift from the products they referenced — nothing kept them in sync once written.
- MongoDB does support multi-document ACID transactions since v4.0, but only across a replica set, and it's an opt-in feature layered on top of a document-per-operation default rather than the storage engine's native mode — teams reach for it less by default than they would in a relational engine.
- Forgoes EF Core, versioned relational migrations, and the query/mapping fluency that's directly what a C# employer expects, which matters independent of the pure data-modelling argument (see [language-platform-choice.md](language-platform-choice.md)).
- **Where it would have won:** if the domain were actually document-shaped — a content catalogue, a config store, a system where records genuinely don't reference each other — Mongo's flexible schema and horizontal scale-out story would be a legitimate advantage. Grocery orders, inventory, and payments aren't that domain.

### MySQL / MariaDB (the credible relational alternative)

The honest "why not the other free relational database" question — both are ACID, both have real transactions and constraints, so this is a closer call than the MongoDB comparison.

- **Type system:** Postgres has a materially richer native type set — `numeric` with arbitrary precision, `uuid`, arrays, ranges, and `enum` types as real first-class types, versus MySQL's narrower set where several of these are either absent or emulated (e.g. no native array type; enums exist but are treated as a labelled integer with generally-discouraged semantics for schema evolution).
- **JSON support:** MySQL's `JSON` type stores text and re-parses it per access; Postgres's `jsonb` stores a decomposed binary format that supports direct indexing (GIN) and is materially faster for anything beyond trivial read-and-return access patterns. This matters for the parts of the schema that are legitimately semi-structured (audit metadata, webhook payloads).
- **Full-text search:** Postgres's generated `tsvector` columns plus GIN indexes, combined with `pg_trgm` for typo-tolerant fuzzy matching, are a materially more capable built-in search story than MySQL's `MATCH … AGAINST`, which has weaker relevance ranking and a less flexible query language. This is what lets the project avoid standing up Elasticsearch for a small catalogue (see `F1` in [`engineering-decisions.md`](../engineering-decisions.md)).
- **Standards compliance:** Postgres tracks the ANSI SQL standard more closely; MySQL has a longer history of divergent defaults (historically permissive type coercion, `GROUP BY` behavior) that have been tightened over versions but still differ from strict-mode Postgres behavior in ways that occasionally surprise.
- **Where MySQL is genuinely comparable or ahead:** raw read throughput on simple primary-key lookups at very large scale is a frequently-cited MySQL strength, and it remains marginally more common in shared/budget hosting environments. Neither axis is decisive for a project of this size and shape — this is the honest near-tie of the three alternatives, not a rout.

### DynamoDB and other key-value/wide-column NoSQL

The same rejection class as MongoDB, briefly, because the two are often raised together.

- Item-level transactions exist (`TransactWriteItems`) but are capped (100 items / 4 MB in a single transaction) and don't span regions — workable for the inventory-reservation write, but a worse fit than a relational engine's native multi-table transaction for a domain with several genuinely relational writes per operation.
- No schema-level constraints at all — every invariant (rating bounds, non-negative stock, uniqueness) would need to be enforced entirely in application code, the same structural gap as MongoDB, just with less tooling around it (no aggregation-pipeline-level validation, no `$jsonSchema` equivalent).
- Query patterns are access-pattern-first: you design tables around the exact queries you'll run, which is the right model for a small number of extremely high-throughput, predictable access patterns (a session store, a leaderboard) and a poor model for a catalogue that needs ad hoc filtering, faceting, and joins across orders/products/stores.
- No general-purpose full-text search, no arbitrary decimal type — both would need to be bolted on via a separate service (OpenSearch, application-level scaled-integer money).

## The non-technical factor — stated separately, if one exists

None beyond what's already covered in [language-platform-choice.md](language-platform-choice.md)'s audience-fit note — a relational, EF-Core-backed schema is also more directly transferable to the kind of C# role this project targets. That's the same factor already stated there, not a separate one specific to the database choice, so it isn't re-argued here.

## In GroceryEasy

See `A3` in [`docs/engineering-decisions.md`](../engineering-decisions.md) and the full record in [ADR-0002](../adr/0002-postgresql-over-mongodb.md), including the rejected "split" option (Postgres for transactional data, Mongo retained for the catalogue) — rejected as two databases and a sync problem for a catalogue of a few thousand rows. Concretely closes `[L-04]` (non-atomic stock decrement going negative), `[L-19]` (unconstrained rating producing a 27-star average), `[L-15]` (cart with no server-side existence, so it can't participate in a transaction with order creation), and `[L-14]` (NoSQL operator injection via unsanitized filter input — not expressible against parameterized SQL). See [`docs/legacy-audit.md`](../legacy-audit.md) for the full defect writeups.

## Interview questions

**Q: Why not just keep MongoDB — wouldn't that have saved the data migration entirely?**
It would have, but it keeps the burden of correctness in application code. Constraints like "rating between 1 and 5" or "stock never negative" would still be conventions someone has to remember to enforce, not guarantees the database makes. The legacy app is the direct evidence this fails: an unconstrained rating field produced a 27-star average because nothing rejected a bad value.

**Q: MySQL is also free and relational — why Postgres over MySQL specifically?**
This is the closest call of the alternatives. Postgres has a richer native type system (arbitrary-precision `numeric`, real arrays, `uuid`), `jsonb` that's indexed and binary-stored versus MySQL's text-stored `JSON`, and a more capable built-in full-text search that let this project skip standing up Elasticsearch for a small catalogue. MySQL is a legitimate alternative and arguably has an edge on raw simple-lookup throughput at very large scale — that scale isn't this project's problem.

**Q: Doesn't MongoDB support transactions now? Isn't that argument outdated?**
It does, since v4.0, but only across a replica set and as a layered-on feature rather than the engine's native mode — teams use it less by default than a relational engine's built-in transactions. More importantly, transactions alone don't give you schema-enforced constraints; Mongo still can't natively express "this rating must be between 1 and 5" the way a `CHECK` constraint does.

**Q: Why not DynamoDB if you wanted something that scales further than Postgres?**
DynamoDB's transaction support is capped and single-region, it has no schema-level constraints at all, and its access-pattern-first design is right for a small number of predictable, extremely high-throughput queries — a session store or leaderboard — not for a catalogue that needs ad hoc filtering and joins across orders, products, and stores. This project's scale (a few thousand catalogue rows) doesn't need DynamoDB's ceiling, and the domain shape doesn't fit its model.

**Q: What's the concrete example of Postgres constraints preventing a real bug?**
`CHECK (rating BETWEEN 1 AND 5)` on the reviews table, backed by a `Rating` value object in the domain that can't be constructed out of range. In the legacy app, an update path skipped the numeric coercion the create path had, string concatenation ran instead of arithmetic, and a product ended up displaying 27 stars out of 5. With a database-level constraint, that write is rejected outright — there's no code path that can persist an invalid rating, new or edited.

**Q: What did you give up by choosing Postgres?**
Schema changes now require a migration rather than just writing a new document shape — adding a field is minutes instead of seconds, and anything destructive needs expand/contract discipline. Embedded documents (reviews on a product, addresses on an order) that were free in Mongo become explicit joins. A one-time migration tool had to be built to move the legacy data over, including converting base64 image blobs into object storage.
