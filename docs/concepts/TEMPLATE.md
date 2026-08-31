# Decision-doc template

Use this structure for every file in `docs/concepts/` that documents a specific engineering/architectural decision (as opposed to a general pattern primer like [xunit-vs-nunit.md](xunit-vs-nunit.md)). Copy the skeleton below, delete this note, fill in the sections. See [language-platform-choice.md](language-platform-choice.md) for a fully worked example.

**When a decision earns one of these docs:** reuse the bar from [ADR-0000](../adr/0000-record-architecture-decisions.md) — it's architecturally significant if it's expensive to reverse, constrains later choices, or would surprise a competent developer reading the code. If it clears that bar, it gets a doc here, in addition to (not instead of) its `docs/adr/` entry and/or its section in `docs/engineering-decisions.md`.

**The rule that keeps this honest:** lead with *technical* criteria — properties the workload actually needs — before any non-technical factor (cost, timeline, audience fit, familiarity). If a non-technical factor genuinely mattered, state it, but label it as such and keep it separate from the engineering case, so the two never get blended into one soft answer.

---

```markdown
# <Decision title>: <chosen option> vs. the alternatives

## The criteria that actually matter for this decision

Not general-purpose criteria for this category of choice — the properties that differentiate
the options *for this project's actual constraints*. Usually 3-6 bullets. Ground each one in
something concrete: a numbered requirement, a defect from `docs/legacy-audit.md`, a workload
characteristic (I/O-bound vs CPU-bound, read-heavy vs write-heavy, etc).

- **<Criterion 1>.** <why it matters here, specifically>
- **<Criterion 2>.** <why it matters here, specifically>

## Comparison at a glance

| Criterion | <Chosen> | <Alt 1> | <Alt 2> | <Alt 3> |
|---|---|---|---|---|
| <criterion> | <…> | <…> | <…> | <…> |

(Omit this table if there are only two options and prose covers it faster.)

## Why not each alternative — the technical case

One subsection per rejected alternative, bullets not paragraphs. Be honest about close calls —
if an alternative is genuinely comparable on some axis, say so; false confidence is what makes
an answer collapse under a follow-up question.

### <Alternative 1>
- <concrete technical reason it lost, tied to the criteria above>
- <where it would actually have won — the honest trade-off>

### <Alternative 2>
- …

## The non-technical factor — stated separately, if one exists

Cost, timeline, team familiarity, target-audience fit — real factors, but not engineering
ones. State plainly that it's separate from the technical case above, so it's never the
answer given when someone asks "why, technically." Omit this section entirely if no
non-technical factor genuinely applied.

## In GroceryEasy

Link back to the formal record: the relevant section of
[`docs/engineering-decisions.md`](../engineering-decisions.md) and/or
[`docs/adr/NNNN-*.md`](../adr/README.md). If the decision closes a specific legacy defect,
cite it: `[L-nn]` per [`docs/legacy-audit.md`](../legacy-audit.md).

## Interview questions

Rapid-fire, rehearsable. Cover: "why not the most credible alternative," the alternative
that's the closest technical peer (the "honest near-tie" question), and at least one
"give a concrete example from the code" question that forces a real answer instead of a
recited one.

**Q: <the question an interviewer would actually ask>**
<direct answer, 2-4 sentences, no hedging>

**Q: <…>**
<…>
```
