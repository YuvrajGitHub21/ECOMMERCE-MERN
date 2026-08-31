# Implementation log

A running record of what was actually built, in what order, and every decision taken along the way — including the ones to **skip** something and defer it.

This is deliberately different from the other documents here:

- [`docs/adr/`](../adr/README.md) records *architectural* decisions, formally and immutably.
- [`docs/engineering-decisions.md`](../engineering-decisions.md) is the narrative of those decisions.
- The phase briefs in this folder say what *should* happen.
- **This file says what did happen, and where reality diverged from the brief.**

Every entry follows the same shape so the log stays scannable:

> **What** was done · **Why** this way · **Alternatives** considered · **Skipped/deferred** and to when

---

## Branch and merge-request strategy

Decided before any code was written, because it shapes every commit that follows.

```
main                     release branch, protected, only ever merged into from development
 └── development         integration branch; every phase merges here by pull request
      ├── feat/phase-1-api-skeleton      → PR into development
      ├── feat/phase-2-catalog-tenancy   → branched from phase 1, PR into development
      ├── feat/phase-3-spa-foundation    → branched from phase 2, PR into development
      ├── feat/phase-4-ordering-spine    → branched from phase 3, PR into development
      ├── feat/phase-5-payments-admin    → branched from phase 4, PR into development
      └── feat/phase-6-hardening-deploy  → branched from phase 5, PR into development
```

**Why each phase branches from the previous phase's branch rather than from `development`:** the phases are strictly dependent — Phase 2 cannot compile without Phase 1's database context, Phase 4 cannot run without Phase 2's catalogue. Branching from `development` would mean each phase branch starts without the phase before it, unless the previous pull request is merged first. Branching from the previous phase branch lets work continue while the pull request is still open for review.

**The cost, stated honestly:** each pull request's diff includes the previous phase's commits until that one is merged. Merging them in order into `development` resolves this, and GitHub shows a clean diff once the base branch is merged. The alternative — waiting for each merge before starting the next phase — is cleaner in the diff view and slower in wall-clock time. Speed wins here because there is one developer and no review queue.

**Alternative considered and rejected: trunk-based development with short-lived branches.** It is the better practice for a team shipping daily, and it is the wrong fit for a project whose unit of work is a two-to-three-week phase with a hard verification gate at the end. The phase gate *is* the review, and it needs a branch to sit on.

### On merge requests

The GitHub CLI (`gh`) is **not installed on this machine** and no attempt was made to install it, because installing developer tooling system-wide is not something to do silently inside an implementation task. Branches are therefore pushed from here, and each phase's pull-request creation link is recorded in this log for one-click creation in the browser. If `gh` is installed later, `gh pr create --base development` does the same job from the terminal.

---

## Phase 1 — .NET skeleton, identity, continuous integration

Branch: `feat/phase-1-api-skeleton` · Base: `development`

Starting state was **mid-phase**, not empty — see [`phase-1-remaining.md`](phase-1-remaining.md) for the verified audit. Tasks 1.1 through 1.3 were already complete and committed or in the working tree.

### Entry 1.0 — Committed the work in flight

**What:** the hand-rolled CQRS dispatcher, the three Scrutor pipeline behaviours, `ValidationError`, the change that un-sealed `Error`, and the dispatcher pipeline tests were sitting uncommitted in the working tree. Committed as one `feat:` commit before any new work started.

**Why this way:** every subsequent diff is unreadable if it is layered on top of an uncommitted foundation. This is Task 1.0 in the brief for exactly that reason.

**Alternatives:** splitting it into three commits — dispatcher, behaviours, tests. Rejected as archaeology: the three were written together and do not compile apart, so separate commits would be a fiction.

**Skipped:** nothing.

---

*Entries are appended below as work proceeds.*
