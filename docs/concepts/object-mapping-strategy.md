# Object mapping: manual extension methods and EF projections vs. AutoMapper and Mapster

## The criteria that actually matter for this decision

Mapping is "turn a `Product` entity into a `ProductResponse` DTO" — trivial in isolation, but the *mechanism* chosen has consequences that compound across a codebase with dozens of these conversions:

- **Compile-time safety, not just runtime correctness.** If a property is renamed on the entity, does the build fail at the mapping site, or does the mapping silently return `null`/default and fail later, in production, on whatever request happens to touch that field first?
- **"Find usages" has to actually work.** When a developer needs to know everywhere `Product.Name` flows to, can the IDE answer that in one keystroke, or does the answer live inside a reflection-driven configuration object the IDE can't see through?
- **Query shape control for reads.** This project's read paths are the highest-traffic code in the system (catalogue browsing, list endpoints). The mapping mechanism decides whether the database is asked for every column or only the ones actually rendered — this is the direct line to `L-18`.
- **Dependency and licensing footprint.** `C4` in [`engineering-decisions.md`](../engineering-decisions.md) already rejected MediatR for going commercial; any mapping library considered here is judged against the same bar.
- **Debuggability.** Can you put a breakpoint on the actual line that sets a field, or does the trace stop at a library's internal expression-tree compiler?

## Comparison at a glance

| Criterion | Manual `ToResponse()` extensions (chosen, commands) | EF Core `.Select()` projection (chosen, reads) | AutoMapper | Mapster |
|---|---|---|---|---|
| Mapping bugs caught | At compile time (build fails on a bad reference) | At compile time (LINQ expression, checked by the compiler) | At runtime, unless `AssertConfigurationIsValid()` is added and kept green | At compile time *only* if using the source-generator mode; runtime-compiled mode otherwise |
| "Find usages" on a property | Works — it's ordinary C# | Works — it's ordinary LINQ | Broken — the assignment lives inside a `Profile`'s fluent configuration, invisible to "find usages" | Same problem in convention-based mode; works if every member is mapped explicitly (which erases most of the tool's value) |
| Query column selection | N/A (mapping happens after the query) | Explicit — only selected columns hit the database | Achievable via `ProjectTo<T>()`, but relies on the library's expression-tree translation being correct and optimal | Has an equivalent `ProjectToType<T>()`, same caveat |
| License / cost | MIT (it's your own code) | MIT (part of EF Core) | Dual-licensed at v15 (July 2025) — free only under a "Community" tier gated on revenue (<$5M) and outside capital (<$10M); paid tiers otherwise | MIT, no conditions |
| Raw mapping throughput | N/A — hand-written, as fast as any C# assignment | N/A — translated to SQL, no in-process mapping cost | Slower — resolves more at map time even with compiled execution plans | Faster than AutoMapper (compiles to delegates/expression trees ahead of time) |
| New dependency | None | None (already have EF Core) | Yes, plus its licensing terms | Yes |

## Why not each alternative — the technical case

### AutoMapper

The default answer for ".NET object mapping" for most of the last decade — worth taking seriously before rejecting it.

- **Same licensing class as the decision already made in `C4`.** AutoMapper went dual-licensed at v15 (July 2025): free only under a "Community" tier — annual gross revenue under $5M, under $10M in outside capital, not a government entity or university. Outside those conditions it's $499–$3,999/year. This is the same vendor, the same trend as MediatR (see [mediatr.md](mediatr.md)) — having just rejected one paid dependency at the request-dispatch boundary, taking a licensing risk of the identical shape at the mapping boundary would be inconsistent.
- **Moves mapping bugs from compile time to runtime.** A convention-based `CreateMap<Product, ProductResponse>()` resolves property matches by name and type at startup or at first map — rename a source property and the mapping doesn't fail to build, it fails (or silently null-maps) the first time that code path executes. `AssertConfigurationIsValid()` closes some of this gap, but it's an opt-in test someone has to remember to write and keep passing, not a property of the language.
- **Breaks "find usages."** The assignment `response.Name = product.Name` doesn't exist as text anywhere if the mapping is convention-based — it's inferred by reflection over property names inside a `Profile`. Right-click → Find Usages on `Product.Name` will never show you the mapping call site, which matters the moment someone needs to change what a field means and trace every consumer.
- **The intent lives in a different file from the code that uses it.** A `ProductProfile : Profile` class is configuration, decoupled from any single call site — understanding what a given DTO actually contains means opening a second file and reading fluent configuration syntax rather than reading the mapping itself.

### Mapster

The real free alternative, and the honest "why not just use this instead" question once AutoMapper is off the table.

- Genuinely fixes AutoMapper's two loudest complaints: **MIT-licensed with no conditions at all**, and **faster** — it compiles mappings to actual delegates/expression trees ahead of time rather than resolving more at map time, and its optional `Mapster.Tool` mode shifts that compilation to build time (true source generation, closing the runtime-safety gap AutoMapper has).
- **The honest near-tie:** once you've accepted that manual mapping's actual benefits — compile-time safety, working "find usages," a debuggable call stack — are worth having, Mapster is *one more dependency for a problem hand-written extension methods already solve*, at zero abstraction cost and zero future licensing risk to track. It is a strictly better AutoMapper, not a strictly better *manual mapping*.
- **Where it would win:** a project with dozens of near-identical DTOs and a tight timeline, where writing an extension method by hand for every one is real, measurable toil. Convention-based codegen saves real time there. GroceryEasy's mapping surface is narrow and organized per vertical slice — each slice needs one or two response shapes — so that time saving mostly doesn't materialize, and the dependency doesn't pay for itself.
- Object mapping is also very rarely the actual bottleneck in this workload: a few hundred microseconds per thousand DTOs disappears next to one database round trip, so the performance argument that favors Mapster over AutoMapper doesn't translate into a reason to prefer it over doing the mapping by hand.

## The chosen approach

Two different mechanisms for two different jobs, not one universal answer:

**Commands and single-entity reads — manual extension methods**, living beside the slice they serve:

```csharp
public static ProductResponse ToResponse(this Product product) => new(
    product.Id,
    product.Name,
    product.SalePrice,
    product.Variants.Select(v => v.ToResponse()).ToList());
```

Ordinary C#: the compiler checks it, "find usages" on `Product.Name` finds this line, and a breakpoint here is a breakpoint on the actual assignment — not on a library's internal compiled expression tree.

**List and search queries — EF Core `.Select()` projection directly in the query**, not "load the entity, then map it":

```csharp
var items = await _db.Products
    .Where(p => p.StoreId == storeId)
    .Select(p => new ProductListItem(p.Id, p.Name, p.SalePrice, p.ThumbnailKey))
    .ToListAsync(ct);
```

This isn't "mapping after the fact is slow" — it's that projecting inside the LINQ query changes the **SQL EF Core generates**: Postgres is asked for exactly the four selected columns, not `SELECT *`. This is the structural fix for `L-18`, where the legacy catalogue endpoint shipped the entire product document — including its base64 image blob — on every single list request, because there was no notion of "only fetch what's rendered." AutoMapper's `ProjectTo<T>()` can achieve the same SQL shape, but then correctness depends on the library's expression-tree-to-SQL translation being right and efficient for every query shape used — one more unknown, in exactly the place payload size and read latency matter most.

## In GroceryEasy

See `C4` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record (shared with the MediatR/FluentAssertions decision — see [mediatr.md](mediatr.md) for that half). Closes [`L-18`](../legacy-audit.md#l-18) (the whole catalogue, including base64 image blobs, shipped on every request because pagination and column selection were both effectively dead).

## Interview questions

**Q: Why not AutoMapper — isn't it the standard tool for this in .NET?**
It was, but it went dual-licensed at v15 (July 2025): free only under a revenue- and capital-gated "Community" tier, otherwise a paid annual fee — the same shape of risk this project already rejected for MediatR. Independent of licensing, it also moves mapping bugs from compile time to runtime and makes "find usages" on a property useless, since the assignment lives inside reflection-driven configuration rather than in code.

**Q: Mapster is free and MIT-licensed though — why not just use that instead of writing mapping code by hand?**
That's the honest near-tie. Mapster genuinely fixes AutoMapper's licensing and performance complaints. But once you've decided manual mapping's real benefits — compile-time safety, working "find usages," a debuggable call stack — are worth having, Mapster becomes one more dependency for a problem hand-written extension methods already solve, for a mapping surface that's narrow enough per vertical slice that the codegen time savings don't materialize.

**Q: Doesn't hand-writing every mapping get repetitive?**
For this project's shape — one or two response types per vertical slice — it's a handful of lines beside the code that uses them, not hundreds of DTOs. The repetition argument is real at a different scale (dozens of near-identical DTOs), which is exactly where Mapster would earn its place.

**Q: Give a concrete example of the query-projection choice mattering.**
The legacy catalogue's list endpoint (`L-18`) returned every field of every product, including a base64-encoded image, because there was no concept of selecting only the columns a list view needs. `Products.Select(p => new ProductListItem(...))` in EF Core translates to a SQL statement that names exactly those columns — Postgres never reads or transmits the image bytes for a list request at all, structurally, not by convention.

**Q: What about AutoMapper's `ProjectTo<T>()` — doesn't that solve the same projection problem?**
It can produce a similar SQL shape, but then you're trusting the library's expression-tree-to-SQL translation to be correct and efficient for every query shape you write, rather than reading the LINQ you wrote yourself. For the highest-traffic read paths in the system, that's an extra unknown in exactly the place it matters least to have one.
