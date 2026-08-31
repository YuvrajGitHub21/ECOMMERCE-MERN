# API client strategy: OpenAPI codegen vs. the alternatives

## The criteria that actually matter for this decision

Not "how should a frontend call a backend" in general — the properties that matter given this
project's specific history and its cross-language boundary:

- **The failure mode has to be structurally impossible, not just less likely.** The legacy app
  didn't lack effort at the client/server boundary — it had a contract, both sides just disagreed
  about it, and nothing caught the disagreement until it was in production. Any solution that
  still relies on two humans independently getting a shape right is the same failure mode with a
  smaller blast radius, not a different one.
- **The backend and frontend are different languages.** C# on ASP.NET Core, TypeScript in the
  SPA. Anything that shares *types* rather than a *contract* needs a common language to share them
  in, and there isn't one.
- **The check has to run somewhere a human can't skip it.** `L-12`'s root cause wasn't that
  nobody could have caught `error.response.data.message` vs. `{success, error}` — it's that
  nothing was *positioned* to catch it: no compiler, no test, no CI gate sat on that exact seam.
- **Cost has to stay near zero as the API grows.** Twelve endpoints today, dozens by Phase 5. A
  scheme that requires a human to manually update a type file per endpoint degrades exactly the
  way the legacy contract did — via a step someone forgot.

## Comparison at a glance

| Criterion | Generated client from OpenAPI | Hand-written types | Shared types package / monorepo | tRPC-style inference |
|---|---|---|---|---|
| Source of truth | The API's actual OpenAPI document, derived from `TypedResults` | A developer's belief about the API, written separately | A shared `.ts` package both sides import | The backend's own TS types, imported directly |
| Requires cross-language type sharing | No — OpenAPI is a language-neutral JSON/YAML contract | No, but that's the problem | Yes — same language on both sides | Yes — same language on both sides |
| Catches a server change the client hasn't adopted | Yes — CI diff/staleness check fails the build | No — nothing links the two files | Only if published as part of the same PR, and only if someone remembers | Yes, but only because it *requires* one shared codebase |
| Applies when backend is C# and frontend is TypeScript | Yes — this is the exact case it's designed for | Yes, and exactly how the legacy bug happened | No — no TypeScript exists on the backend to share from | No — tRPC's entire mechanism is inferring TS types from a TS server |
| Ongoing maintenance cost as endpoints grow | Near zero — regenerate on every OpenAPI change, automatically | Grows linearly with endpoint count, entirely manual | Grows, plus a second package to version and publish | Near zero, but only inside a single-language stack |

## Why not each alternative — the technical case

### Hand-written TypeScript types, manually kept in sync

This is not a hypothetical alternative — it is *exactly what the legacy app did*, so it's worth
being concrete about how it failed rather than arguing it in the abstract.

- The legacy server responded `{ success: false, error: err.message }`. Every one of the ~30
  call sites in the legacy frontend's action creators read `error.response.data.message` — a key
  that was never present on the response at all (`[L-12]` / `[L-17]` in
  [`legacy-audit.md`](../legacy-audit.md)). Two independently-written string literals, `error` and
  `message`, describing the same field, agreeing with nothing but each developer's memory of what
  the other side "probably" sent.
- Nothing was in a position to catch it. TypeScript's compiler checked that the hand-written
  `ErrorResponse` interface was *internally consistent* — every call site correctly matched
  *that* interface — while the interface itself was simply wrong about what the server actually
  returned. A type system only tells you code is consistent with your assumption; it says nothing
  about whether the assumption is true.
- The practical result: **every error toast in the entire application displayed the literal word
  `undefined`**, because `error.response.data.message` was `undefined` and nobody printed anything
  more descriptive. A second failure stacked on top — on any network/timeout error,
  `error.response` was itself `undefined`, so the `catch` block threw, the `FAIL` action never
  dispatched, and the UI hung on a spinner forever.
- Where it's genuinely fine: for a single, stable, rarely-changing endpoint maintained by one
  person, hand types are low-ceremony and this failure mode is unlikely to actually trigger. It
  degrades badly precisely under the conditions this project has — multiple slices, active
  iteration, an API surface that grows every phase.

### A shared types package (monorepo type-sharing)

The most credible-sounding alternative, and worth taking seriously rather than dismissing.

- This pattern (a `packages/shared-types` workspace, or tRPC's approach below) is excellent when
  both the frontend and backend are TypeScript — `Order`, `PlaceOrderRequest`, and friends live in
  one file, and both sides `import` the same declaration. There is no format-translation step, so
  there is nothing to drift.
- It structurally does not apply here: **the backend is C#, not TypeScript.** A "shared types
  package" needs a language both projects can import types from. There is no TypeScript type
  declaration living inside `GroceryEasy.Api` to share — the API's types are C# records and DTOs,
  compiled to IL, not to `.d.ts` files.
- The honest generalization: this alternative doesn't lose on merit, it's simply answering a
  different question ("how do I share types within one language") than the one this project has
  ("how do I keep two different languages' idea of a shape in sync"). OpenAPI generation is
  the cross-language version of the same instinct — a machine-readable contract instead of a
  literally-shared type declaration.

### tRPC-style end-to-end type inference

Popular enough in TypeScript-only stacks (Next.js + tRPC, T3 stack) that it's worth naming
directly rather than leaving an obvious question unanswered.

- tRPC's mechanism is inferring the client's types directly from the *server's own TypeScript
  router* — `AppRouter`'s type parameter flows into the client hook's return type via generics, no
  code generation step, no schema in between.
- That mechanism is inseparable from both ends being the same language and the same repo (or at
  minimum, importable as a type-only dependency) — it is TypeScript inferring from TypeScript.
  There is no version of tRPC that infers a client's types from a C# Minimal API, because C#
  generics and TypeScript's structural type inference don't interoperate at that level.
- Same root reason as the shared-types option: it's a strong pattern that simply doesn't have
  a foothold in a polyglot stack. If this project's backend were ever a Node/TypeScript service,
  tRPC would be a legitimate contender against OpenAPI codegen; it isn't, so it doesn't get to
  compete here.

## The mechanism, precisely — why generation + a CI staleness gate closes the gap the others can't

The distinguishing property isn't "generated code is fancier" — it's *where the check lives*.

1. **`TypedResults` in the Minimal API endpoints (`C3` in
   [`engineering-decisions.md`](../engineering-decisions.md)) makes the response shape a
   compile-time C# fact**, not a comment or a convention. The compiler itself won't let a handler
   return a shape the endpoint's declared `TypedResults<T>` doesn't allow.
2. **That fact flows automatically into the OpenAPI document** — ASP.NET Core's built-in OpenAPI
   generation reads the same compiled type information, so the document is a *derived* artifact of
   the actual code, not a second thing a developer writes by hand and can get out of sync.
3. **The TypeScript client is generated from that document in CI**, not written by a person
   reading the document and translating it into a `.ts` file by eye. There is no manual
   translation step for a typo or a mismatched key to survive.
4. **A build-breaking staleness check** diffs the committed generated client against what
   generating fresh from the current OpenAPI document would produce, and fails CI if they differ.
   This is the step that actually prevents the legacy bug's *shape* of failure: a PR that changes
   the API's response contract without regenerating the client cannot merge. The check doesn't
   care whether the developer remembered — it fires whether they did or not.

Contrast that chain against "two developers agree on an interface": at every one of those four
steps, the hand-written-types version instead has *a person, trusting their own memory of the
other side*. The legacy bug is the demonstrated failure rate of that trust. Generation collapses
steps 1–3 into one fact with one source; the CI gate in step 4 makes step 3 unskippable. That is
the actual mechanism — not "codegen is more modern," but "the number of places a human has to get
an assumption right, on this specific seam, drops from several to zero."

## In GroceryEasy

See `H2` in [`engineering-decisions.md`](../engineering-decisions.md) for the formal record, and
`C3`'s note that `TypedResults`' compile-checked response types are precisely what "flows
automatically into the OpenAPI document" — the two decisions are load-bearing on each other. This
is the direct, permanent structural fix for `[L-12]`/`[L-17]` in
[`legacy-audit.md`](../legacy-audit.md): the server returned `{success, error}`, every client read
`.message`, and two mismatched string literals made every error in the app display the word
`undefined`. See also [`language-platform-choice.md`](language-platform-choice.md) for why the
backend is C# in the first place, which is the reason the shared-types and tRPC alternatives don't
apply.

## Interview questions

**Q: Why not just write the TypeScript interfaces by hand and review them carefully in PR?**
Because that's exactly what the legacy app did, and it's the exact bug it produced: the server
sent `{success, error}`, every client call site read `.message`, and both sides were "carefully
written" — the mismatch just wasn't visible to either developer at the time they wrote it. Careful
review catches bugs a reviewer thinks to look for; it doesn't catch an assumption nobody
questioned. Generation removes the step where a human transcribes their belief about the shape
into code at all.

**Q: Isn't a shared types package basically the same idea, just without the codegen step?**
It's the closest technical peer, and it's excellent when it applies — but it needs both sides to
be the same language, importing the same declaration. This backend is C#; there's no TypeScript
type living on the server to share. OpenAPI generation is the version of that same idea that
works across a language boundary: a machine-readable contract standing in for a shared type
declaration neither side can literally import.

**Q: tRPC gives full end-to-end type safety with zero codegen — why not that?**
tRPC infers the client's types directly from the server's own TypeScript router type — it's
TypeScript inferring from TypeScript, in the same repo. There's no version of that mechanism that
reaches into a C# Minimal API; C# generics and TypeScript's structural inference don't
interoperate. It's the right call in an all-TypeScript stack and simply isn't available here.

**Q: What actually stops someone from just not regenerating the client after an API change?**
The CI staleness check: it regenerates the client from the current OpenAPI document as part of the
build and diffs it against what's committed. If they differ, the build fails — not a lint warning,
a merge-blocking failure. The check doesn't depend on the developer remembering; it depends on the
document and the committed client agreeing, which is machine-checkable regardless of who forgot
what.

**Q: What's the concrete, in-the-code example of the bug this prevents?**
The legacy server's error middleware returned `{ success: false, error: err.message }`; roughly
thirty call sites across four frontend action files read `error.response.data.message`. That key
didn't exist on the response. Every error toast in the app displayed the literal string
`undefined`, and on network failures the mismatch escalated further — `error.response` itself was
undefined, the `catch` threw, and the UI hung on a spinner forever. A generated client can't have
that specific bug, because there's no hand-written interface for the two sides to disagree about
in the first place.
