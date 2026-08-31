# Technical debt triage: freeze-and-document vs. fixing the legacy defects

## The general question this answers

"How do you decide whether to fix legacy code or replace it?" is a triage decision every engineer eventually has to make, and it generalizes past this project: given a known list of defects in an existing system, do you (a) fix everything before doing anything else, (b) fix only what's dangerous and move on, or (c) not fix any of it and put the effort somewhere else? The right answer depends on a small number of concrete variables, not a general philosophy — and the variable that dominates here is **the remaining lifespan of the code you'd be fixing.**

## The criteria that actually matter for this decision

- **Cost of fixing vs. cost of the code being deleted soon.** Time spent patching code has a return on investment only if that code keeps running long enough to earn it back. If the code is scheduled for deletion, the fix's ROI is bounded by how long it survives — here, roughly six weeks.
- **Opportunity cost against the fixed budget.** ~170 hours total; every hour spent on Express patches is an hour not spent on the rewrite, and the rewrite is the actual deliverable.
- **What's demonstrable to a reviewer.** A portfolio project is evaluated by what it shows about the author's judgment, not just what it contains. "I found N bugs and patched them" and "I found N bugs, understood exactly which architectural gap let each one exist, and can point to the specific design element in the new system that makes that gap unrepresentable" are very different signals, even if the second produces zero net code that runs in production.
- **Whether the running (frozen) system needs to stay safe for anyone besides the author.** If the frozen app were public and actively linked, "leave it exploitable" would be irresponsible regardless of the deletion timeline — the threat model changes the calculus.

## Comparison at a glance

| Criterion | Freeze and document (chosen) | Fix all 20 defects | Fix only the ~6 critical ones |
|---|---|---|---|
| Time cost | Near zero (writing time only) | ~2–3 weeks of a 14-week budget | ~1–1.5 weeks |
| Value of the work once the code is deleted | Fully retained — the document survives the deletion | Mostly lost — patched code is deleted in ~6 weeks regardless | Mostly lost, same as above |
| What it demonstrates to a reviewer | Root-cause understanding of every defect class, mapped to a structural fix | "This person can patch bugs" — true, but table stakes | Same, narrower |
| Risk if the frozen demo is ever poked at | Real, but mitigated by not being publicly linked | Lower — most-dangerous paths patched | Partially lower |
| Overlap with the actual rewrite's work | None — it's disposable | None — it's disposable | None — it's disposable |

## Why not each alternative — the technical case

### Fix all 20 defects in Express first

The instinct many engineers default to — "don't leave known bugs lying around" — and worth taking seriously before rejecting.

- Costs an estimated 2–3 weeks of a 14-week budget, patching code that is deleted in roughly six weeks regardless. The fixed code never gets a chance to earn back that investment — there's no window where it runs long enough in a state that matters for anyone to benefit from the fix.
- None of the patching work transfers to the rewrite. Fixing the Express `forEach`-doesn't-await bug in the legacy stock decrement teaches nothing that carries into designing the Postgres conditional `UPDATE` that replaces it — they're different languages, different data stores, different concurrency primitives entirely.
- It optimizes for a codebase's safety that has an intentionally short remaining lifespan, at the direct expense of the codebase that has to actually work for the next decade of a candidate's career trajectory.
- **Where it would win:** a system that is *not* scheduled for deletion — actual production code with an uncertain or long remaining lifespan. If GroceryEasy's Node backend were staying in production while the rewrite happened in parallel (which is exactly the strangler-fig scenario — see [migration-strategy.md](migration-strategy.md)), fixing the critical defects wouldn't be optional, it would be required regardless of the rewrite's existence.

### Fix only the ~6 critical ones

The middle-ground instinct — "at least close the worst holes" — and a more defensible partial version of the same argument.

- Still spends real budget (~1–1.5 weeks) on code with the same six-week remaining lifespan, so the core ROI problem from the full-fix option is only partially reduced, not eliminated.
- Picking "critical" from the audit's severity list *is* the same intellectual work as writing the structural-prevention analysis — you have to understand root cause well enough to judge severity — but this option throws that understanding away as a one-line patch instead of preserving it as a legible artifact anyone (including a future interviewer) can read.
- Still produces zero code that survives into the rewrite, same as the full-fix option.
- **Where it would win:** if the frozen system had any real exposure — if it were a public demo linked from a resume or actively crawled, patching the handful of defects with genuine exploit potential (forgeable order totals, the review IDOR) would be the pragmatic middle ground between "do nothing" and "do everything." That's a real trade-off worth naming, not a strawman.

## The non-technical factor — stated separately

Framing the audit as a portfolio artifact is a genuine, stated factor here, separate from the pure engineering ROI case above: *"I audited my own three-year-old code, found a forgeable order total and an admin guard that had never executed, and here is the architecture that makes each one impossible"* is a narrative that demonstrates senior-level root-cause thinking. *"I added a null check"* demonstrates only that a bug was noticed and closed. Both are honest outputs of triage work — but the first is the stronger interview artifact, and that's an audience/presentation factor, not a claim that documenting is technically superior to fixing in every context. In a real production system, patching known critical defects wouldn't be optional regardless of how good the writeup would look.

## In GroceryEasy

See `B2` in [`docs/engineering-decisions.md`](../engineering-decisions.md). The alternative deliverable is [`docs/legacy-audit.md`](../legacy-audit.md) itself — 20 defects, each mapped to the specific design element in the new system that makes that defect's *class* unrepresentable, not just that one instance fixed. The frozen demo's residual risk is mitigated by it not being publicly linked, which is the concrete version of the "does the threat model actually change the calculus" criterion above.

## Interview questions

**Q: You found 20 bugs in your own code and didn't fix any of them — isn't that just leaving known vulnerabilities in production?**
The code carrying those bugs is deleted within about six weeks of the audit — it's frozen, not maintained. Patching it would cost 2–3 weeks of a 170-hour budget to harden code with a fixed, short remaining lifespan, and none of that patching work transfers to the rewrite. The audit converts that same root-cause understanding into a permanent artifact instead: every defect mapped to the specific structural change in the new system that makes its whole class impossible, not just that one instance.

**Q: What if you'd at least fixed the critical ones — wouldn't that be the responsible middle ground?**
It's a real, defensible option, and it would have been the right call if the frozen app had any real exposure — say, if it were a linked, actively-used public demo. Here it wasn't: not publicly linked, no users. Given that, fixing the critical six still spends real budget on code with the same six-week lifespan as the rest, for a partial version of the same ROI problem the full fix has.

**Q: How do you generally decide whether to fix or replace legacy code, outside this specific project?**
The dominant variable is the code's remaining lifespan relative to the cost of fixing it — if it's being deleted or replaced soon, patching it is money that can't earn a return. Second is opportunity cost against whatever else that time could buy. Third is exposure — if the code is live and reachable by real users or real data, the calculus changes regardless of its remaining lifespan, because the cost of *not* fixing it is now external, not just wasted effort.

**Q: Isn't "we'll just document it" a rationalization for not doing the harder work of actually fixing bugs?**
It would be, if the documentation were the bugs restated. It isn't — each entry in the audit does the same root-cause work a fix requires (why did this happen, what's the actual gap) and then goes further: it names the specific structural change in the new system that makes the entire *class* of bug unrepresentable, not just patches the one instance found. That's strictly more analysis than a one-line fix, aimed at where it actually pays off — the system being built, not the one being deleted.

**Q: Give a concrete example where documenting instead of fixing produced a better outcome than a quick patch would have.**
The stock-decrement bug (`L-04`): an unawaited `forEach`, no null check, and a lost-update race that could drive stock negative. A quick fix would be adding `await` and a null check — maybe 20 minutes, and it would have made that one code path safer in Express. Instead the audit traces it to the actual root cause (read-modify-write with no atomicity) and specifies the replacement: a single atomic conditional `UPDATE` where "zero rows affected" *is* the out-of-stock answer, proven by a 20-concurrent-order integration test. That design is what's actually in the new system; the 20-minute patch would have been deleted with the rest of the Express code six weeks later.

**Q: Would you make the same call on a real production system with paying customers?**
No — this decision depends entirely on the code having a short, known remaining lifespan and no real exposure. Neither holds for a live production system: there, users are actually harmed by unfixed critical defects regardless of a future rewrite's timeline, so patching the dangerous ones isn't optional. The triage logic here — check ROI against remaining lifespan and exposure before deciding — is the transferable part; the specific answer (fix none of them) is specific to this project's circumstances.
