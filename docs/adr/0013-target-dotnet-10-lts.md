# 0013 — Target .NET 10 (LTS)

- **Status:** Accepted
- **Date:** 2026-08-15

## Context

The rewrite needs a target framework fixed before the first project file exists, because it propagates into `Directory.Build.props`, every NuGet version, the Docker base images and the CI workflow. Changing it later is mechanical but touches everything.

Two candidates were realistic at the time of writing:

| Release | Track | Support ends |
|---|---|---|
| .NET 9 | STS (Standard Term Support) | **2026-05-12 — already past** |
| .NET 10 | LTS (Long Term Support) | 2028-11 |

The local SDK is already `10.0.204`.

## Decision

**Target `net10.0`.**

.NET 9 was a Standard Term Support release and left support on 2026-05-12, three months before this project started. Beginning a rewrite on an unsupported framework would mean shipping something with no security patches on day one — and, for a project whose explicit purpose is to be read by other engineers, advertising that I do not track the support calendar of my own platform.

The LTS track also matters for a portfolio artifact specifically: this repository needs to still build, still deploy and still look current when someone opens it eighteen months from now. Support until November 2028 means it does not quietly rot on GitHub.

Practical benefits that the plan already depends on:

- `Guid.CreateVersion7()` for sequential UUID primary keys — dense B-tree inserts without exposing an enumerable integer id.
- `Microsoft.Extensions.Caching.Hybrid` (L1 + L2 with stampede protection and tag-based invalidation) in the box.
- `Microsoft.AspNetCore.OpenApi` as the built-in OpenAPI generator; Swashbuckle is no longer in the default template.
- `TimeProvider` for injectable, testable time — which the slot-cutoff and reservation-expiry logic relies on.

## Consequences

**Positive**
- Security and bug fixes through November 2028; no forced migration mid-project.
- Access to the current BCL and ASP.NET Core features the design assumes.
- Chiseled, non-root `aspnet:10.0-noble-chiseled` runtime images — small and with no shell in the final layer.

**Negative**
- A small number of third-party packages may lag on official .NET 10 support. Mitigated by preferring first-party or actively maintained libraries, which is already a constraint (see the pending ADR on dependency governance).
- Some blog posts, Stack Overflow answers and AI-generated samples still target .NET 6–8 idioms, so copied snippets need checking against current APIs.
- Any future contributor needs the .NET 10 SDK; the version is pinned in `global.json` so the failure is a clear message rather than a confusing build error.
