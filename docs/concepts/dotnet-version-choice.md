# .NET version choice: .NET 10 (LTS) vs. the alternatives

This is a narrower decision than "why .NET" — that question, and the case against Node, Python, Rust, Go and Java, is covered separately in [language-platform-choice.md](language-platform-choice.md). This doc assumes C#/.NET is already the platform and asks the question an interviewer asks next: *which version, and why does that even matter?*

## The criteria that actually matter for this decision

- **The framework must be supported for the life of the project and its afterlife as a portfolio artifact.** This isn't a service with an ops team watching CVE feeds — it's a repository that has to still build, still deploy, and still look current whenever someone opens it, which could be eighteen months from now.
- **No forced mid-project migration.** A framework version change mid-build means re-validating every package, every Docker base image, and every CI step, on top of the actual feature work. That cost should be paid zero or one times, not twice.
- **Concrete language/runtime features the design already assumes**, not just abstract "newer is better" — specific APIs the plan is written against.
- **SDK availability at the moment the repository was started** — a version that isn't installable yet isn't a real option.

## .NET's release model, for anyone who hasn't tracked it

.NET ships a new major version every November. Alternating releases carry different support commitments:

| Track | Cadence | Support window | Example |
|---|---|---|---|
| **STS** (Standard Term Support) | Odd-numbered releases (`9`, `11`, …) | 18 months from release | .NET 9 — released Nov 2024, supported to May 2026 |
| **LTS** (Long Term Support) | Even-numbered releases (`8`, `10`, …) | 3 years from release | .NET 10 — released Nov 2025, supported to Nov 2028 |

STS releases exist to carry new language/runtime features out faster; LTS releases are the ones production systems are meant to sit on. This is the same shape as Node's even/odd LTS cadence or Ubuntu's LTS releases — a pattern common enough that an interviewer will expect a candidate to know it exists even outside .NET specifically.

## Comparison at a glance

| Criterion | .NET 10 (LTS) | .NET 9 (STS, prior default) | .NET 8 (LTS, prior LTS) |
|---|---|---|---|
| Support window from project start (2026-08) | Until 2028-11 — over 2 years runway | **Already expired** (2026-05-12) | Expired 2026-11 — would already be near end-of-life |
| SDK available at project start | Yes, `10.0.204` | Yes, but unsupported | Yes, but stale |
| `Guid.CreateVersion7()` (sequential UUIDv7 keys) | Built in | Built in | Not available — needs a third-party package |
| `Microsoft.Extensions.Caching.Hybrid` (`HybridCache`) | Built in, GA | Introduced (preview/early) | Not available |
| Built-in OpenAPI document generation | `Microsoft.AspNetCore.OpenApi`, default template | Same, newer | Needs Swashbuckle |
| `TimeProvider` for injectable/testable time | Available | Available | Available (introduced .NET 8) |
| Non-root chiseled container images | `aspnet:10.0-noble-chiseled` | Available | Available, older base |
| Ecosystem/package maturity for .NET 10 specifically | Newest, some lag possible | N/A (moot — unsupported) | Most mature |

## Why not each alternative — the technical case

### .NET 9 (the prior default track)

The most tempting alternative, because it's one version behind and "newer, so probably fine" is an easy trap.

- .NET 9 is STS: 18 months of support from its November 2024 release, ending **2026-05-12** — a date that had already passed by the time this project started (2026-08). Targeting it means starting a rewrite on a framework receiving no further security patches, on day one, with no path except "migrate later" already baked in.
- For a project whose explicit purpose is to be read by other engineers, shipping on a framework past its own support window reads as not tracking the platform's own release calendar — a bad signal in exactly the artifact meant to demonstrate technical judgment.
- **Where it would have won:** if the project had started in early-to-mid 2025 while .NET 9 was still the current release and .NET 10 was months from shipping, STS would have been the only option with a stable SDK. That window had already closed by August 2026, which is what makes this not a close call today.

### .NET 8 (the prior LTS)

The "why not just stay on the boring, most-proven LTS" question.

- .NET 8 was released November 2023 and its three-year support window ends November 2026 — a few months after this project starts. Building an 8-week-plus project against a framework with only months of remaining support defeats the entire point of choosing LTS for longevity in the first place.
- Missing the concrete APIs the design leans on: no `Guid.CreateVersion7()` (would need a third-party package or hand-rolled UUIDv7 generation for the sequential-PK strategy), no `HybridCache` (would fall back to raw `IDistributedCache` with stampede protection hand-built), and OpenAPI generation would need Swashbuckle rather than the first-party generator.
- **Where it would have won:** .NET 8 is the most battle-tested option on this list — two-plus years of the ecosystem catching up to it, every blog post and Stack Overflow answer written against current idioms, and zero risk of a third-party package lagging support. If the project needed maximum package-ecosystem maturity over access to newer APIs, 8 would be the defensible choice. It just isn't the right trade here, because its support window is nearly spent.

## The non-technical factor — stated separately

None beyond what's already the technical case. Unlike the language/platform choice, there's no audience-fit angle specific to picking 10 over 9 or 8 — a C# employer doesn't care which LTS a candidate happened to target, only that the candidate can explain *why* they picked the one they did. That's a technical-judgment question, not a portfolio-fit one, which is why it's argued entirely on support windows and API availability above.

## In GroceryEasy

See `A2` in [`docs/engineering-decisions.md`](../engineering-decisions.md) and the full record in [ADR-0013](../adr/0013-target-dotnet-10-lts.md). The target framework is pinned in `global.json` so a contributor without the .NET 10 SDK gets a clear failure message rather than a confusing build error.

## Interview questions

**Q: Why .NET 10 and not .NET 9 — isn't 9 the more current release?**
.NET 9 is a Standard Term Support release; it left support on 2026-05-12, three months before this project started. Starting a rewrite on an already-unsupported framework means no security patches from day one, and for a project meant to demonstrate engineering judgment, that's a bad signal to ship. .NET 10 is LTS, supported to November 2028.

**Q: What's actually the difference between LTS and STS in .NET — isn't that just marketing?**
It's a real support commitment, not marketing: LTS releases (even-numbered, .NET 8, 10, 12…) get three years of patches; STS releases (odd-numbered, 9, 11…) get eighteen months. STS releases exist so new features ship faster; LTS releases are what you're meant to actually build on top of for anything long-lived.

**Q: Why not the older, more battle-tested .NET 8 LTS instead of the newest one?**
.NET 8's three-year support window ends November 2026 — a few months into this project's timeline — so it defeats the purpose of picking LTS for longevity. It's also missing concrete APIs the design already assumes: `Guid.CreateVersion7()` for the UUIDv7 primary-key strategy, and built-in `HybridCache`. Both would need to be replaced with third-party packages or hand-rolled equivalents on .NET 8.

**Q: Doesn't targeting the newest LTS risk third-party packages not supporting it yet?**
Yes, and that's the one real cost — a few packages may lag official .NET 10 support. It's mitigated by preferring first-party or actively-maintained libraries anyway, which was already a constraint for other reasons (see the MediatR/AutoMapper licensing decision in [object-mapping-strategy.md](object-mapping-strategy.md)).

**Q: Give a concrete example of a .NET 10 feature this project actually depends on.**
`Guid.CreateVersion7()` — used for primary keys so they're time-ordered for dense B-tree inserts without exposing an enumerable sequential integer id the way `SERIAL` would. That's a built-in BCL method on .NET 9+/10; on .NET 8 it would need a third-party package like `UUIDNext`.

**Q: What happens when .NET 10's support window ends in November 2028 and this repo is still sitting on GitHub?**
It stops receiving patches, same as .NET 8 would today — that's the honest cost of any fixed support window on a portfolio artifact nobody actively maintains. The choice isn't "never expires," it's "expires as late as possible relative to when someone is likely to actually open and evaluate the repo," which LTS maximizes versus STS.
