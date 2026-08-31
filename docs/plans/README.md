# Planning documents

The original working plans, written before and during each phase, kept here so the project's planning history is visible in the repo — not just on the machine that wrote it — and so anyone picking up the project can see the reasoning behind the phase sequencing without asking.

These are **working documents, not the source of truth.** For what actually happened and why, defer to:

- [`docs/engineering-decisions.md`](../engineering-decisions.md) — the current narrative record of every decision, with alternatives and cost.
- [`docs/adr/`](../adr/README.md) — the formal, immutable decision record.

Two kinds of file live here, and the difference matters:

- **Frozen records** — written before the work, kept verbatim, never updated. `master-plan.md` and `phase-1-brief.md`.
- **Live briefs** — written against the code as it actually is, and edited as work lands. Everything named `phase-N-brief.md` from Phase 2 onward, plus `phase-1-remaining.md`.

## Contents

| Doc | Kind | What it is |
|---|---|---|
| [master-plan.md](master-plan.md) | Frozen | The full project plan: legacy audit summary, repo layout, architecture calls, data model, correctness engineering (pricing, concurrency, idempotency, outbox, state machine), features, testing strategy, operations, phase sequencing 0–6, and the Phase 0 execution brief. |
| [phase-1-brief.md](phase-1-brief.md) | Frozen | The original brief that scoped Phase 1. Kept verbatim as the working record. Superseded on "what is left to do" by `phase-1-remaining.md`. |
| [phase-1-remaining.md](phase-1-remaining.md) | Live | Phase 1 finished off: persistence, identity and the token layer, the API layer, architecture tests, the Testcontainers harness, continuous integration, decision records. Opens with the verified state of the code. |
| [phase-2-brief.md](phase-2-brief.md) | Live | Catalogue domain, multi-tenancy, object storage, full-text search, admin catalogue management, migrator and seeder. |
| [phase-3-brief.md](phase-3-brief.md) | Live | The new single-page application: tooling, design system, generated API client, authentication, catalogue and account screens. |
| [phase-4-brief.md](phase-4-brief.md) | Live | The spine: cart, pickup versus delivery, pricing engine, inventory reservation, idempotency, outbox, order state machine, checkout. |
| [phase-5-brief.md](phase-5-brief.md) | Live | Payments end to end, background jobs, real-time updates, the admin console. |
| [phase-6-brief.md](phase-6-brief.md) | Live | Hardening, observability, end-to-end tests, containers, deployment, and the documentation that presents it all. |
| [phase-gates.md](phase-gates.md) | Live | The exact commands that decide whether a phase is finished. One place to check work against, whoever did it. |

Every brief follows the same shape: verified starting state, hard constraints and non-goals, numbered tasks each with its own acceptance check, a definition of done, known pitfalls, and a reporting-back protocol.
