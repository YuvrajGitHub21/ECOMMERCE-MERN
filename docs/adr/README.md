# Architecture decision records

Why the system is built the way it is. Each record captures one decision, the options weighed against it, and what it costs — see [ADR-0000](0000-record-architecture-decisions.md) for the conventions.

ADRs are written when the decision is made and are immutable once accepted. Changing course means a new record that supersedes the old one; both stay here.

## Accepted

| # | Decision | Summary |
|---|---|---|
| [0000](0000-record-architecture-decisions.md) | Record architecture decisions | MADR format, written contemporaneously, immutable, always with negative consequences listed |
| [0001](0001-clean-room-rewrite-over-strangler-fig.md) | Clean-room rewrite over strangler-fig | A strangler protects live traffic; there is none. It would cost ~a quarter of the budget on scaffolding built to be deleted. The seam is the frontend cutover instead |
| [0002](0002-postgresql-over-mongodb.md) | PostgreSQL over MongoDB | Orders, inventory and payments need transactions and constraints the document model could not express — the direct structural answer to defects L-04, L-15 and L-19 |
| [0003](0003-vertical-slices-inside-clean-architecture.md) | Vertical slices inside Clean Architecture | Keep the four projects and their dependency rules; organise Application by feature, one file per slice, so a feature is read and changed in one place |
| [0004](0004-dependency-governance.md) | Dependency governance — no MediatR, AutoMapper or FluentAssertions | All three moved to commercial licences in 2025. Replaced with a hand-written dispatcher, query projection and Shouldly, rather than pinning stale versions that stop receiving security fixes |
| [0005](0005-result-over-exceptions.md) | `Result<T>` for expected failures, exceptions for bugs | An email already taken is an outcome; a null argument is a defect. One `ErrorType`-to-status mapping renders every failure as RFC 9457 ProblemDetails — the permanent fix for L-17 |
| [0006](0006-no-generic-repository.md) | No generic repository — `DbContext` is the unit of work | `IRepository<T>` over EF Core throws away projection, `Include` and indexable filters, and buys a portability nothing will use. Narrow aggregate repositories only where there is real loading logic |
| [0013](0013-target-dotnet-10-lts.md) | Target .NET 10 (LTS) | .NET 9 was STS and left support 2026-05-12. LTS runs to Nov 2028, so the project does not rot on GitHub |
| [0014](0014-minimal-apis-over-controllers.md) | Minimal APIs with an `IEndpoint` convention | Controllers' action selection and filter pipeline are cost without benefit here, and the dispatcher already owns validation, logging and transactions. Convention-based discovery means adding an endpoint is adding a file |

## Planned

Recorded here so the numbering is stable and the intent is visible before the code lands.

| # | Decision | Phase |
|---|---|---|
| 0007 | Inventory concurrency — reservation table with atomic conditional `UPDATE` | 4 |
| 0008 | Razorpay over Stripe, with Cash on Delivery as a first-class method | 5 |
| 0009 | PostgreSQL full-text search over Elasticsearch | 2 |
| 0010 | Single-database multi-tenancy with a `store_id` discriminator | 2 |
| 0011 | Run migrations at release, never at application startup | 6 |
| 0012 | Hangfire over bare hosted services for background work | 5 |

## Related

- [`docs/engineering-decisions.md`](../engineering-decisions.md) — the narrative overview of every decision with its rejected alternatives, plus the phase-by-phase roadmap. Start there for the whole picture; come here for the formal record of any single decision.
- [`docs/legacy-audit.md`](../legacy-audit.md) — the 20 defects in the pre-rewrite system, each mapped to the design decision that makes its class of bug unrepresentable.
- [`docs/plans/`](../plans/README.md) — the original working plans written before each phase began.
