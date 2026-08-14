# 0000 — Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-08-15

## Context

GroceryEasy is being rewritten from a Node/Express + MongoDB app into an ASP.NET Core + PostgreSQL platform. The rewrite involves a long series of decisions — some obvious, several genuinely contested, and a few that deliberately reject the popular option.

Six months from now, in a code review or an interview, "why is it like this?" is a question that deserves a better answer than my memory. Reconstructed reasoning is always cleaner and less honest than the reasoning that actually happened.

## Decision

Record every architecturally significant decision as a numbered Markdown file in `docs/adr/`, following [MADR](https://adr.github.io/madr/). A decision is architecturally significant if it is expensive to reverse, constrains later choices, or would surprise a competent developer reading the code.

Rules:

1. **Write the ADR when the decision is made, not afterwards.** Retrospective ADRs are justifications; contemporaneous ones are decisions. They take about fifteen minutes while the reasoning is fresh.
2. **Record the options that were rejected, and why.** An ADR listing only the chosen option is a press release.
3. **Every ADR ends with Consequences, split positive and negative.** A decision with no downsides means the analysis is incomplete.
4. **ADRs are immutable once accepted.** Changing your mind means a new ADR that supersedes the old one; both stay in the repository. The history of a decision is part of the decision.

## Consequences

**Positive**
- Design intent survives the gap between writing code and explaining it.
- Rejecting a trendy option becomes defensible, because the reasoning is written down rather than implied by its absence.
- New readers can start with `docs/adr/` instead of reverse-engineering intent from the code.

**Negative**
- ~15 minutes of overhead per decision, and it must be spent at the moment of least patience — right when the decision feels obvious.
- Immutability means the folder accumulates superseded documents that are no longer true. This is the intended trade-off, but it does mean the index must clearly mark status.
