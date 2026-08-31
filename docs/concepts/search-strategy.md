# Search strategy: PostgreSQL full-text search vs. the alternatives

## The criteria that actually matter for this decision

Not "how do you build search" in the abstract — the properties that matter for *this* catalogue, at *this* size, coming out of *this* audit:

- **Corpus size.** The catalogue is 100–2,000 SKUs. That number should directly gate the architecture — a technology choice that's right at 50 million rows can be the wrong choice at 2,000.
- **Structural injection safety.** The legacy search box compiled raw user input as a MongoDB regex (`L-13`) and the filter chain did a blind string-substitution into Mongo query operators (`L-14`). Whatever replaces it has to make that *class* of bug inexpressible, not just patch the two known instances.
- **Consistency with the source of truth.** A search result pointing at a product that no longer exists, or missing one that does, is a correctness bug wearing a UX costume.
- **Operational footprint for a solo-maintained system.** Every additional stateful service is something that has to be provisioned, monitored, upgraded, and reasoned about during an incident — with no ops team to share that load.
- **Typo tolerance.** A grocery search box gets "amool" for "Amul" and "toothpast" for "toothpaste" constantly; exact lexeme matching alone isn't enough for a consumer-facing product.

## Comparison at a glance

| Criterion | Postgres FTS + `pg_trgm` (chosen) | Elasticsearch | Meilisearch / Typesense |
|---|---|---|---|
| Extra infrastructure | None — same database | Separate cluster/container, JVM-based | Separate lightweight service |
| Memory floor for ~2,000 rows | Negligible (shares Postgres's buffer cache) | Gigabytes, largely fixed cost regardless of corpus size | Tens–hundreds of MB, lighter than ES but still standalone |
| Index-sync problem | None — the index is a column on the row itself | Yes — a pipeline (CDC, dual-write, or batch reindex) must keep the ES index consistent with Postgres | Yes — same class of problem, smaller blast radius |
| Consistency with source of truth | Transactional — updates in the same write | Eventually consistent at best; can silently drift if the sync pipeline breaks | Same eventual-consistency risk |
| Injection safety | `tsquery` is parsed, not evaluated as syntax | Query DSL is JSON, not string-interpolated — also safe *if built correctly* | Typically safe — parameterized SDKs |
| Query latency at this corpus size | Sub-5ms, in-process | Comparable, plus a network hop | Comparable, plus a network hop |
| Relevance tuning depth | Basic — ranked lexeme matching, trigram similarity | Deep — custom scoring functions, learn-to-rank, faceting at scale | Moderate — good defaults, less tunable than ES |
| Horizontal scale-out | None without a later migration | Built for it — sharding, replicas | Limited, improving |

## Why not each alternative — the technical case

### Elasticsearch

The "industry standard for search" answer, and the one most likely to get pushback in an interview.

- It's a whole separate stateful system: a JVM-based cluster (or single node, but still a separate process/container) that needs its own memory allocation, its own upgrade cadence, its own monitoring, and its own failure modes — independent of whether the database it's searching has 2,000 rows or 200 million.
- The memory floor is largely fixed, not proportional to data size. A minimal ES node still wants multiple gigabytes of heap to run comfortably; that cost doesn't shrink because the catalogue is small.
- It requires an index-sync pipeline: something has to keep the Elasticsearch index consistent with Postgres as the source of truth — either dual-writes on every mutation, a change-data-capture stream, or scheduled reindexing. Every one of those is more code to write, and more code that can silently fall out of sync, producing search results for products that were deleted an hour ago or missing ones added five minutes ago.
- That sync gap is a real consistency problem, not a theoretical one: Postgres and Elasticsearch have no shared transaction, so there is always a window — however small — where the two disagree.
- None of this buys anything at 2,000 rows. Elasticsearch's actual value proposition — distributed query execution across shards, relevance tuning at a scale where naive ranking falls over, sub-second search across a corpus too large for one machine's working set — doesn't engage at all here. Reaching for it anyway reads as reaching for a résumé keyword, not solving the problem in front of you.

### Meilisearch / Typesense

Worth naming explicitly, because "just use a lighter search engine, not full Elasticsearch" is the natural next objection.

- Both are a real improvement over Elasticsearch on operational weight — Rust-based, single binary, low memory footprint, fast to stand up. If the corpus were, say, 500,000 rows and Postgres FTS's ranking quality started to feel thin, either would be a legitimate next step before reaching for Elasticsearch.
- But the core objection to a separate search system doesn't go away just because the system is lighter: it's still a second store of the same data, still needs a sync pipeline to stay consistent with Postgres, and still adds a service to provision and monitor. The problem was never "Elasticsearch specifically is heavy" — it's "a second, separately-synced index isn't justified at this data scale," and that argument applies to Meilisearch and Typesense just as much as it applies to Elasticsearch.
- At 2,000 rows, the sync-pipeline cost is pure overhead with no corresponding benefit, regardless of how lightweight the destination system is.

## What Postgres FTS actually does, technically

It's easy to name-drop `tsvector`/GIN/`pg_trgm`; the substance is in what each piece actually does:

- **`tsvector`** is a preprocessed, searchable representation of text: the source text is tokenized into words, stop words (*the*, *and*, *of*) are stripped, and remaining words are reduced to a normalized lexeme via a language-specific stemming dictionary (*"running"*, *"ran"* → `run`), with the position of each lexeme retained for ranking. It's stored as a **generated column** (`GENERATED ALWAYS AS (to_tsvector('english', name || ' ' || description)) STORED`), computed from the row's own columns as part of the same write — there is no separate job or pipeline to keep it in sync, because it isn't a separate copy of the data; it's derived from the row, in the row, in the same transaction.
- **A GIN index** (Generalized Inverted Index) on that column turns "which rows contain lexeme `milk`" into an index lookup — a posting list of row IDs per lexeme — instead of a sequential scan of every product's text. This is the mechanism, not "Postgres is just fast": it's the same fundamental data structure (an inverted index) that a dedicated search engine uses, just implemented as a Postgres index type instead of a separate service.
- **`ts_rank_cd`** ranks matches by *cover density* — it doesn't just count how many query lexemes matched, it weights matches where the lexemes appear close together in the source text more highly than matches where they're scattered far apart. That's what makes "organic whole milk" rank a product literally named that above one where those three words happen to appear in unrelated parts of a long description.
- **`pg_trgm`** handles what lexeme matching can't: typos. It breaks strings into overlapping 3-character sequences (trigrams) — `"amul"` → `am`, `amu`, `mul`, `ul ` — and scores similarity by how many trigrams two strings share. `"amool"` and `"amul"` share enough trigrams to score as similar even though no lexeme matches exactly, which is what makes the typo-tolerant fallback work. Indexed via GIN/GiST, so this fallback is also an index lookup, not a scan.
- **`EF.Functions.WebSearchToTsQuery`** is the piece that closes the injection hole from the audit, and it's worth being precise about *why*: it maps to Postgres's `websearch_to_tsquery`, which is a **parser**, not an evaluator. It takes a raw string — including a web-search-style syntax for quoted phrases and exclusions (`"organic milk" -almond`) — and parses it into a `tsquery`: a structured tree of lexemes ANDed/ORed together. Whatever a hostile caller sends becomes search terms; there is no path from user input to executable query syntax, because the function's entire contract is "turn this string into terms," never "turn this string into code."

That last point is the direct structural fix for `L-13`/`L-14`. The legacy bug wasn't "the wrong search feature" — it was that raw user input was handed to something that *evaluates syntax*: a MongoDB `$regex` operator that compiles the string as a regular expression (enabling catastrophic backtracking — `?keyword=(a+)+$` pins the CPU on an unauthenticated request — and letting `.*` dump the entire catalogue), and a filter chain that blindly string-substituted the literal token `gt` into the Mongo operator `$gt` anywhere it appeared, letting a query parameter smuggle arbitrary Mongo operators (`?ratings[ne]=0`) into the query. In both cases, user text became query *syntax*. `websearch_to_tsquery` and EF Core's parameterized queries structurally can't do that — user text can only ever become *data* (search terms or bound parameter values), never syntax, so the bug class doesn't need to be caught by review; it's inexpressible by construction.

## When Elasticsearch would actually be the right call

Worth stating plainly, because "but isn't Elasticsearch the industry standard?" is the obvious follow-up:

- **Corpus size in the millions-plus**, where a GIN index no longer comfortably fits query-hot pages in memory and full-text query latency on Postgres starts to degrade.
- **Need for horizontal scale-out** — search load (or corpus size) that outgrows a single database instance's capacity, where sharding search across nodes independently of the transactional store's scaling needs becomes necessary.
- **Relevance tuning beyond ranked lexeme matching** — custom scoring functions, learn-to-rank models, faceted aggregation over large result sets, multi-field boosting with runtime weights — the kind of tuning Elasticsearch's query DSL is built for and Postgres FTS simply doesn't expose.
- **Search as an independently-scaled product surface** — e.g., search has its own availability/latency SLA distinct from the transactional database, or search query volume is high enough that you don't want it competing for the same database's connection pool and buffer cache as order-placement traffic.

None of those thresholds are close to 100–2,000 SKUs. The honest answer to the follow-up is a number, not a vibe: this decision is re-evaluated if the catalogue is a few orders of magnitude larger, not because Elasticsearch is bad, but because its cost only pays for itself past a size this project isn't at and isn't targeting.

## In GroceryEasy

See `F1` in [`../engineering-decisions.md`](../engineering-decisions.md) (ADR-0009, Phase 2). Closes [`L-13` and `L-14`](../legacy-audit.md) from the legacy audit. The plan calls for an integration test that fires injection payloads (`'; DROP`, `.*`, `{$gt:""}`) at the search endpoint and asserts empty/benign results rather than errors or elevated CPU.

## Interview questions

**Q: Isn't Elasticsearch the industry standard for search? Why would you not use it?**
It's the standard once you're past a size where it pays for itself — large corpora, horizontal scale, deep relevance tuning. This catalogue is 100–2,000 SKUs; Postgres answers full-text queries against that in under 5ms with no extra infrastructure. Adding Elasticsearch here means standing up a separate stateful cluster, giving it a multi-gigabyte memory floor that doesn't shrink with the data size, and building an index-sync pipeline to keep it consistent with Postgres — all cost, no benefit at this scale.

**Q: What about a lighter option like Meilisearch or Typesense instead of full Elasticsearch?**
Both are genuinely lighter operationally — single binary, low memory, fast to run. But the core objection isn't "Elasticsearch specifically is heavy," it's that any second, separately-synced index is unjustified at this data scale. Meilisearch and Typesense still duplicate the data and still need a sync pipeline to stay consistent with Postgres as the source of truth. They'd be a legitimate step *before* Elasticsearch if the corpus grew into the hundreds of thousands of rows — not a fix for the objection at 2,000.

**Q: How does `tsvector`/GIN actually give you fast search — what's happening under the hood?**
`tsvector` tokenizes and stems the source text into normalized lexemes with position data, computed as a generated column so it's always in sync with the row — it's derived data, not a copy. The GIN index turns that into an inverted index: a lexeme-to-row-IDs lookup, so a search for "milk" is an index lookup, not a table scan. `ts_rank_cd` then ranks matches by cover density — how closely the matching terms cluster in the text — so a product literally named "organic whole milk" outranks one where those words are scattered across an unrelated paragraph.

**Q: How does this actually prevent the injection bug from the legacy app?**
The legacy bug was raw user input reaching something that *evaluates syntax* — a MongoDB `$regex` operator compiled the search string as a live regular expression (enabling ReDoS), and a filter-chain string-substitution let query parameters smuggle in Mongo operators like `$gt`. `EF.Functions.WebSearchToTsQuery` is a parser: whatever string it receives becomes search terms, never query syntax. There's no code path from user input to executable syntax, so the bug class isn't fixed by validation — it's structurally impossible.

**Q: What handles typos — "amool" finding "Amul"?**
`pg_trgm`, a trigram similarity extension. It breaks both the query and the stored text into overlapping 3-character sequences and scores similarity by shared trigrams, so "amool" and "amul" score as similar even with zero exact lexeme match. It's used as a fallback when the primary `tsvector` match returns nothing, and it's indexable (GIN/GiST), so it's still an index lookup, not a scan.

**Q: At what point would you actually revisit this and bring in Elasticsearch?**
When the corpus moves from thousands to millions of rows, or when search needs to scale independently of the transactional database's load, or when relevance tuning needs go past ranked lexeme/trigram matching into custom scoring or faceted aggregation at scale. None of those are close to true here — this is a threshold decision, and the honest answer names the threshold instead of dismissing the alternative.
