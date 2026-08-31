# Image storage strategy: object storage vs. base64-in-database

## The criteria that actually matter for this decision

Not "where can bytes live" in the abstract — the properties that matter for a commerce catalogue's actual read/write shape:

- **Image bytes must not couple to the primary transactional database's operational budget.** Every backup, every WAL segment, every streaming replica of the database that holds orders and inventory should carry data relevant to transactional correctness — not megabytes of JPEG that never changes and never needs point-in-time recovery.
- **Catalogue images must be CDN-cacheable without extra plumbing.** A product image is read enormously more often than it's written — every catalogue browse, every search result, every cart line. If the bytes can sit behind a CDN at a stable URL, that read cost approaches free; if they can't, every one of those reads round-trips the origin.
- **The upload path must not funnel large binary payloads through the API process.** Every image byte that passes through the API server costs that server's request-size limits, memory, and outbound bandwidth — resources that should be sized for JSON payloads, not photo uploads.
- **Cost has to scale with actual usage**, particularly for a demo deployment with no revenue behind it — a storage/bandwidth bill that punishes success (more traffic = more cost) is a worse fit than one that doesn't.
- **The legacy failure mode is documented, not hypothetical** (`[L-18]`): base64 images embedded in Mongo documents, combined with a broken pagination limit, meant every catalogue request shipped every product's image bytes, unindexed and unbounded.

## Comparison at a glance

| Criterion | Object storage + `object_key` (chosen) | Base64 in the database | Raw binary blob column (`bytea`) |
|---|---|---|---|
| Payload size vs. raw bytes | 1× | ~1.33× (base64 encoding overhead) | 1× |
| CDN-cacheable | Yes — stable, immutable URL | No — bytes are embedded in a per-request JSON response | No, without a hand-built streaming endpoint |
| Couples to primary DB backup/replication | No | Yes | Yes |
| Document/row size ceiling risk | None | Real — MongoDB's 16 MB document ceiling | Real — large `bytea` values push into Postgres's TOAST storage |
| Upload bandwidth through the API | None — client uploads directly to storage | Full payload through the API | Full payload through the API |
| Local-dev parity with production | MinIO mirrors the same S3 API | Trivial (same DB either way) | Trivial (same DB either way) |
| Egress cost at read time | R2: zero egress fees | Bundled into API server egress | Bundled into API server egress plus DB read cost |

## Why not each alternative — the technical case

### Base64-encoded images stored directly in the database

This is what the legacy app did, and it fails on every axis at once.

- **~33% payload inflation from base64 encoding.** Every three bytes of image data becomes four bytes of text — pure overhead, on disk and on the wire, with zero benefit in exchange.
- **Uncacheable by any CDN**, structurally. The image bytes are embedded inside a JSON response alongside the product's name, price, and stock — fields that change per edit, per tenant, per request. There is no stable, image-only URL to cache; every catalogue page re-downloads every image on it, every time, because the "image" isn't a resource of its own, it's a field on a resource that's assumed to be dynamic.
- **Pushes documents toward MongoDB's 16 MB ceiling.** A handful of full-resolution photos on one product, or a product with several review images attached, is a realistic path to hitting a hard limit that has nothing to do with how much data the product actually needs to represent.
- This is `[L-18]` exactly: the legacy catalogue endpoint had `resultPerPage = 999` (pagination was dead), so **every** product's base64 blob shipped on **every** catalogue request — and the base64 inflation made an already-broken pagination bug meaningfully more expensive in bytes than it needed to be.
- **Where it's genuinely fine:** a single tiny, rarely-changing image — a favicon, one small logo — where standing up a separate storage round-trip is disproportionate to the few hundred bytes involved. Not a catalogue of hundreds of photographed SKUs.

### Raw binary blobs in a relational database column

The honest middle ground, worth naming directly because "just use `bytea` instead of base64" is a real suggestion an interviewer might make, and it does fix one of the three problems.

- It removes the base64 inflation — these are real bytes, not text-encoded ones.
- It does **not** remove the coupling to the primary database. The image bytes still live in the same database instance as orders, inventory, and pricing — so every backup, every point-in-time-recovery snapshot, every streaming replica now carries gigabytes of static file content that has nothing to do with transactional correctness. Backup and restore time, and replication lag, both grow with catalogue image volume even though nothing about that volume affects order correctness.
- It's still **not CDN-cacheable without extra work**. Serving an image still means a request to the API, a query against Postgres, and a response written by hand with correct `Cache-Control`/`ETag` headers — you'd be building a bespoke image-streaming endpoint to get back to where a plain object-storage URL starts by default.
- Large `bytea` values interact with Postgres's TOAST mechanism (values over roughly 2 KB get compressed and stored out-of-line automatically) — workable, but it's extra machinery solving a problem that simply doesn't exist if the bytes were never in the relational database to begin with.
- **Where it would win:** a genuinely small number of images that must live inside the same transaction and backup boundary as the row that owns them, for strict consistency reasons — not the shape of a product catalogue.

## Chosen: object storage with only `object_key` in the database

Image bytes live in an S3-compatible object store — **Cloudflare R2 in production, MinIO locally**, both speaking the identical S3 API, so the same client code and the same integration tests run against either. The database stores nothing but a string: `object_key`.

**The presigned PUT upload pattern**, and why it matters concretely:

1. The client asks the API for permission to upload an image for a given product/store.
2. The API validates the caller's authorization and generates a **server-chosen** `object_key` (content-addressed or scoped by store id + a new GUID — never a client-supplied path), then calls the storage SDK to produce a **time-limited, signed URL** authorizing exactly one `PUT` to exactly that key.
3. The client uploads the image bytes **directly to R2/MinIO**, using that signed URL — the request never touches the API server at all.
4. The client notifies the API the upload completed; the API persists the `object_key` against the product/review row.

This removes two real costs, not just one theoretical one:

- **The image never counts against the API's own request-size limits.** Kestrel and any reverse proxy in front of it have a body-size ceiling sized for JSON payloads; raising it to accommodate photo uploads would also raise it for every other endpoint, widening a memory-exhaustion attack surface for no benefit (the legacy app's own unrelated `express-fileupload` misconfiguration, mounted with no size limit and used by nothing, is exactly this class of unforced risk — see the *Unused attack surface* finding in [`../legacy-audit.md`](../legacy-audit.md)).
- **Bandwidth for the upload never touches the API server's own budget.** On a free-tier hosting plan in particular, image upload/download traffic is exactly the kind of load an API server shouldn't be absorbing on behalf of what is, functionally, static file storage.

**CDN cacheability and cost, concretely:** because the `object_key` is stable and content-addressed, the resulting public URL can carry aggressive, effectively-immutable cache headers (`Cache-Control: public, max-age=31536000, immutable`) — a catalogue image is requested once by the CDN edge and served from cache for every subsequent request, anywhere. R2 specifically advertises **zero egress fees**, which is a concrete, named factor for a demo deployment: a link that gets shared and hammered unpredictably doesn't turn into a bandwidth bill the way it would on a provider that bills per GB served.

## In GroceryEasy

See `F2` in [`../engineering-decisions.md`](../engineering-decisions.md) for the formal record. Closes [`L-18`](../legacy-audit.md#l-18) (the whole catalogue, including base64 image blobs, shipped on every request because pagination was dead). Phase 2 of the roadmap wires this up alongside ImageSharp-generated renditions (thumbnail/detail sizes) at upload time, so the catalogue never serves a full-resolution photo where a thumbnail would do.

## Interview questions

**Q: Why not just store the bytes in Postgres as `bytea` instead of base64 — doesn't that fix the main problem?**
It fixes the encoding overhead, but not the other two problems. The bytes still live in the same database instance as orders and inventory, so every backup and every replica now carries image data that has nothing to do with transactional correctness — and it's still not CDN-cacheable without hand-building a streaming endpoint with correct cache headers. Object storage removes all three costs at once instead of one.

**Q: What's a presigned PUT, and why does it actually matter rather than just being a cleaner API?**
The API generates a short-lived, signed URL scoped to one specific storage key, and the browser uploads directly to that URL — image bytes never pass through the API process. Concretely, that means the API's own request-size limits never have to be widened to accommodate photo uploads (which would also widen them for every other endpoint), and the bandwidth cost of the upload is never the API server's to pay.

**Q: Doesn't adding object storage mean another moving part to operate?**
Yes, and that's a real cost — it's why MinIO exists for local development, speaking the identical S3 API so the same code path is exercised without needing a live R2 account to run tests or develop locally. In production it's one bucket behind Cloudflare, not a service that needs its own scaling story.

**Q: What stops a client from using a presigned URL to upload to some other tenant's storage path, or overwrite an arbitrary key?**
The `object_key` is generated server-side, not supplied by the client — it's scoped to the authenticated store/product and typically content-addressed or GUID-suffixed. The presigned URL authorizes a `PUT` to that exact key only, for a short expiry window; there's no code path where the client chooses the destination.

**Q: Give a concrete example of the legacy bug this closes.**
`[L-18]`: the legacy product endpoint hardcoded `resultPerPage = 999`, so pagination never worked and every catalogue request shipped every product's base64-encoded image inline. The rewrite makes that specific failure mode unrepresentable twice over — pagination is a typed, server-enforced `PagedResult<T>` (closing the "ships everything" half), and images are `object_key` strings resolved to CDN URLs rather than bytes on the row at all (closing the "base64 inflation on every response" half).

**Q: Why R2 specifically, rather than raw AWS S3?**
Both speak the same API, so the code doesn't care — the concrete, named reason for R2 is zero egress fees. A demo deployment with unpredictable, possibly-viral traffic is exactly the shape of workload that per-GB egress billing punishes hardest; R2 removes that risk entirely rather than requiring careful monitoring of a bill that scales with success.
