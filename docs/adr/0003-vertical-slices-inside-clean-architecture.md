# 0003 — Vertical slices inside Clean Architecture

- **Status:** Accepted
- **Date:** 2026-09-01

## Context

Clean Architecture fixes the *dependency* direction: Domain at the centre, Application around it, Infrastructure and the API outside, with references pointing inward only. It says nothing about how to organise files inside a layer.

The default in most .NET codebases is to group by technical type — `Services/`, `Interfaces/`, `Models/`, `Validators/`, `Handlers/`. Adding one feature then means touching five folders, and reading one feature means visiting five.

Pure Vertical Slice Architecture answers that by organising strictly by feature and dropping the layer boundaries, often letting a slice talk to the database directly.

## Decision

**Keep the four projects and their dependency rules. Inside `GroceryEasy.Application`, organise by feature.**

```
GroceryEasy.Application/
  Abstractions/          the seams: messaging, data, identity, email
  Behaviors/             the pipeline every slice runs through
  Features/
    Auth/
      Login/Login.cs                    command + validator + handler
      Refresh/Refresh.cs
      Register/Register.cs
      ...
    Users/
      GetCurrentUser/GetCurrentUser.cs
```

A slice is one file holding the command, its validator and its handler. Reading `Login.cs` shows the whole feature: what comes in, what is checked, what happens, what goes out.

**One file per slice, not four.** The vertical-slice convention often splits `LoginCommand.cs`, `LoginCommandValidator.cs`, `LoginCommandHandler.cs`, `LoginResponse.cs` into a folder. Those types are written together, change together and are meaningless apart, so keeping them together optimises for the actual reading unit. The folder-per-slice layout is retained so a slice that genuinely outgrows one file has somewhere obvious to expand into.

Shared per-feature concerns sit at the feature root: `Features/Auth/AuthErrors.cs` names every failure the auth slices can return, once.

## Why not the alternatives

**Grouping by technical type.** A `Handlers/` folder with forty files sorted alphabetically puts `LoginCommandHandler` next to `ListProductsQueryHandler`, which have nothing to do with each other. Every feature change becomes a five-folder edit, and deleting a feature reliably leaves orphans behind.

**Pure vertical slices with no layers.** Genuinely good for a service where most endpoints are thin reads and writes. Wrong here for two reasons. First, this system has real domain logic — a pricing engine and an order state machine — that must be unit-testable with no database and no clock, which needs a Domain project with no dependencies, enforced. Second, "Clean Architecture with enforced boundaries" is a thing a reviewer can check in thirty seconds by reading the project references, and NetArchTest asserts it.

**A modular monolith with a project per bounded context.** The right answer at a larger size. Here there is one bounded context — a grocery storefront — so splitting it would produce ceremony without isolation.

## Consequences

**Positive**

- Adding a feature is adding a folder. Deleting one is deleting a folder.
- A pull request diff is localised, so a reviewer sees the whole change in one place.
- The layer boundary that carries the value — Domain depends on nothing — is kept and mechanically enforced.
- Handlers stay `sealed internal`, so the dispatcher is the only way in and the pipeline cannot be bypassed. An architecture test asserts this.

**Negative**

- Two organising principles coexist, so a newcomer has to learn both: layers across projects, features within one.
- Cross-slice reuse needs a deliberate home. `AuthErrors` and `AuthenticationResult` at the feature root are that home, and the discipline is to promote only after the second use rather than in anticipation.
- A file holding four types is unusual in C#, where one-type-per-file is the default expectation. The compensating rule is that a slice file only ever contains one slice.
- Slices can drift apart in shape — two handlers solving similar problems differently — which grouping by type would have made obvious. The pipeline mitigates this by making the cross-cutting parts uniform.

## Related

- [`docs/concepts/vertical-slice-architecture.md`](../concepts/vertical-slice-architecture.md)
- [`docs/concepts/clean-architecture.md`](../concepts/clean-architecture.md)
