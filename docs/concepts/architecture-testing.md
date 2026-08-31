# Architecture testing: NetArchTest fitness functions vs. relying on discipline

## What a fitness function / architecture test actually is

A term from Neal Ford, Rebecca Parsons, and Patrick Kua's *Building Evolutionary Architectures*: an **automated test that asserts a structural rule about the codebase, not a business-logic outcome.** A normal unit test asks "does `OrderPricingEngine.Calculate()` return the right total for this cart?" An architecture test asks a different kind of question entirely — "does any class in `Domain` reference a NuGet package?", "does any class outside `Infrastructure` reference `Microsoft.EntityFrameworkCore` directly?", "is every command handler `sealed` and `internal`?" It runs in the same test runner, in the same CI job, and fails the build exactly like a broken unit test — but what it's checking is a property of the dependency graph and type shapes, not of any single method's behavior.

The distinction that matters: a unit test can only fail if someone runs it. An architecture rule expressed only as a convention — a paragraph in a wiki, a comment in `Program.cs`, "we agreed in a meeting" — can be violated by someone who never reads the wiki, and nothing stops the build. A fitness function is checked on every single commit, by construction, whether or not the person making the change knows the rule exists.

## The criteria that actually matter for this decision

- **Clean Architecture's entire value proposition (`C1`) depends on the dependency arrows actually pointing the direction the diagram says they do.** A diagram showing `Domain ← Application ← Infrastructure ← Api` is a claim about the codebase, not a description of it — the claim is only true if nothing has ever violated it, on any commit, by any contributor.
- **Rules enforced only by review erode under exactly the conditions a solo, time-boxed project actually has**: deadline pressure, fatigue, a late-night commit nobody else looks at closely. The rule doesn't need a hostile actor to break — it just needs one tired evening.
- **A new contributor (or a reviewer months later) doesn't know unwritten rules.** "Domain has zero package references" isn't discoverable by reading the code that violates it for the first time — the violation looks like an unremarkable `using` statement, not an alarm.
- **The cost of checking has to be near zero**, or it won't get maintained. NetArchTest rules here run in under a second as part of the existing test suite — there's no separate tool, pipeline stage, or config file to keep in sync.

## Comparison at a glance

| Criterion | Code review + team discipline | Chosen: NetArchTest as executable rules |
|---|---|---|
| Enforced on every commit | Only if a reviewer notices | Yes — CI fails the build |
| Depends on institutional memory | Yes — unwritten rules a new contributor doesn't know | No — the rule is the test |
| Survives deadline pressure | Erodes — "just this once" | Doesn't erode — the test doesn't get tired |
| Catches a violation immediately | Only if review is thorough that day | Immediately, same as a broken unit test |
| Cost to maintain | Ongoing vigilance, forever | ~50 lines, written once |
| Scales with team size / solo memory lapses | No — one missed review is enough | Yes — mechanical, not memory-based |

(Only two real options here — a rule is either mechanically checked or it isn't — so the table is short by design.)

## Why not the alternative — the technical case

### Code review and team discipline alone

- **Discipline is a renewable resource that runs out under pressure**, and a 14-week, ~12-hour-a-week solo project has exactly the profile — tired evenings, deadline-adjacent commits — where a rule remembered on week 2 quietly stops being remembered by week 10. This isn't a hypothetical: the legacy audit is full of exactly this failure mode in a different guise — `L-06`'s admin route guards were *written*, present in the code, and simply wrong in a way no one caught because nothing checked that they actually worked.
- **A rule that isn't mechanically checked is, eventually, silently violated** — not through malice, but through the ordinary mechanics of a codebase changing over time: a new package gets added to `Domain` to solve an immediate problem, with every intention of "refactoring that out later," and later never comes, because nothing flags it as a problem in the meantime.
- **A new team member (or, on a solo project, the same developer returning after a break) doesn't know a rule that only lives in someone's head or an old commit message.** A convention has to be *rediscovered* by reading enough of the codebase to notice a pattern — an architecture test states the rule as an assertion with a failure message, which is strictly more discoverable.
- **Where discipline alone genuinely is enough:** rules that are cheap to check by eye and rarely violated because the type system already makes violations awkward — naming conventions, for instance, rarely need an automated fitness function, because the cost of a human catching a bad name in review is low and the cost of missing one is low too. The rules chosen for automated enforcement here are specifically the ones where a violation is both easy to introduce accidentally and expensive if it survives — a package reference from `Domain`, or a handler that isn't `internal`, doesn't announce itself the way a bad variable name does.

## What NetArchTest actually checks, concretely

A roughly 50-line xUnit test class (not a separate linting tool — ordinary `[Fact]` methods using the `NetArchTest.Rules` fluent API against the compiled assemblies) asserting rules including:

- **`Domain` references nothing outside the BCL and has zero package references.** Enforced with `Types.InThisAssembly().Should().NotHaveDependencyOnAny(...)` against every other project's assembly name — if `Domain` ever takes a dependency on EF Core, ASP.NET Core, or any third-party package, this fails. This is what makes the pricing engine and order state machine pure, mockless unit tests in the first place (`C1`).
- **`Application` never references EF Core or `Infrastructure` directly.** `Application` depends on `IApplicationDbContext` (`C6`) and interfaces like `IPaymentGateway`, never on `Npgsql.EntityFrameworkCore.PostgreSQL` or the `Infrastructure` project itself — checked the same way, by asserting `Application`'s compiled types have no dependency on those assembly names.
- **Domain entities have no public property setters.** `Order.Status` can only change through `TransitionTo(...)` (`D5`); a public setter would let any caller assign an illegal state directly, silently reopening the exact hole the state machine exists to close. Checked with a rule over `Types.InAssembly(...).That().Inherit(typeof(Entity)).Should()...` asserting no public setter exists on any property.
- **Handlers are `sealed` and `internal`.** `sealed` because a command handler is a leaf — nothing should extend it, and allowing inheritance is an invitation to override behavior in a way that silently changes what a handler does without the call site knowing. `internal` because a handler is only ever invoked through the dispatcher (`C4`), never referenced directly by another project — a `public` handler is a signal that something outside `Application` might be calling it directly, bypassing the pipeline behaviors (validation, logging, the idempotency behavior).
- **Every `ITenantEntity` has a query filter registered.** A rule that inspects the EF Core model at test time and asserts every type implementing `ITenantEntity` has a corresponding `HasQueryFilter` call — the mechanical backstop for the tenancy isolation guarantee in [multi-tenancy-strategy.md](multi-tenancy-strategy.md): a new tenant-owned entity that forgets to implement the interface, or a model-building convention that silently stops applying, fails a test instead of becoming a live cross-tenant leak discovered later.

## Why these run as ordinary tests in CI, not a separate linting step

- **A build that's "green except for architecture" is a contradiction this project doesn't allow to exist.** If architecture rules lived in a separate tool — a custom script, a standalone linter invoked in its own pipeline stage — it would be possible (and, under time pressure, likely) for someone to see a green checkmark on the main test suite and ship, with the architecture check either not run, run but not gating merge, or quietly ignored as "just a warning."
- **Running them as `[Fact]` methods in the same test project means an architecture violation fails the build exactly the way a broken unit test does** — same signal, same visibility, same "you cannot merge this" consequence. There's no separate mental category of "architecture broken but tests pass" for a reviewer or a future contributor to reason about.
- **It costs nothing extra to wire up.** No new pipeline stage, no new tool to install and keep updated, no separate report to check — the existing `dotnet test` invocation in CI already runs these ~50 lines alongside everything else, in under a second.

## The non-technical factor — stated separately, if one exists

None. This is a mechanism to keep an already-chosen architecture (`C1`) honest over 14 weeks of solo development — the decision is entirely about enforcement, not about cost or audience fit.

## In GroceryEasy

See `G3` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record, alongside `C1` (Clean Architecture — the rules this suite enforces), `C6` (why `Application` never sees EF Core directly), `D5` (why domain entities have no public setters), and [multi-tenancy-strategy.md](multi-tenancy-strategy.md) (the query-filter rule). Landing in Phase 1 alongside the Testcontainers harness ([integration-testing-strategy.md](integration-testing-strategy.md)) means the architectural boundaries are checked from the very first commit that has more than one project, not retrofitted once a violation has already shipped.

## Interview questions

**Q: Why not just enforce this in code review — isn't that what senior engineers are for?**
Review catches what a reviewer happens to notice on the day they look, which is a function of how tired they are and how familiar the specific violation looks — a new package reference in `Domain` doesn't look alarming unless you already know the rule. A fitness function checks the same rule identically on every single commit, forever, whether or not anyone remembers it exists that week. Discipline is the right tool for judgment calls; it's the wrong tool for a rule that should never be violated, ever, full stop.

**Q: Give a concrete example of a rule this suite actually enforces, and what breaks without it.**
Domain entities have no public property setters — `Order.Status` can only change through `TransitionTo(status, actor, reason)`, which checks the legal-transition matrix (`D5`). Without that rule mechanically enforced, a future handler could write `order.Status = OrderStatus.Shipped` directly, bypassing the state machine entirely and silently reopening the exact bug class the legacy app had, where an order could get stuck with no valid path to `Shipped` — except now in the opposite direction, an illegal jump straight to a status with no validation at all.

**Q: Why run these as normal xUnit tests instead of a dedicated architecture-linting tool?**
Because a separate tool creates a separate pass/fail signal that can be green-lit independently of the real test suite — "tests pass, architecture linter is a warning" is exactly the ambiguity this is designed to remove. Running them as ordinary `[Fact]`s means an architecture violation fails the build the same way a broken unit test does: no separate category, no separate step to forget to check, no way to merge with a known violation and a green checkmark at the same time.

**Q: Isn't 50 lines of test a small thing to hang the whole architecture on?**
That's the point, not a weakness — it's cheap precisely because it only has to state the rule once, mechanically, rather than requiring ongoing vigilance from every future contributor. The alternative isn't "no risk," it's "the same risk, paid repeatedly, forever, by every person who touches the codebase without knowing the rule exists." Fifty lines that never need to be re-explained is a good trade against an unbounded number of future code reviews that might catch the same violation, or might not.

**Q: What's the tenancy-specific rule, and why does it matter more than the others?**
A rule asserting every entity implementing `ITenantEntity` has a registered EF Core query filter — the mechanical backstop for the isolation model in [multi-tenancy-strategy.md](multi-tenancy-strategy.md). It matters more because the failure mode if it silently breaks isn't a crash or a compile error, it's Store A quietly reading Store B's data — discovered by a customer complaint or a security researcher, not a stack trace. A test that fails loudly the moment a new tenant-owned entity forgets to implement the interface is the difference between that being a five-second fix in review and a live data leak.
