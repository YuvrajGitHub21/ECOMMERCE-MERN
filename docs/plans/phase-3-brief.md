> **Status: live working document,** written before Phase 3 begins and meant to be edited as work lands. Decisions remain owned by [`docs/engineering-decisions.md`](../engineering-decisions.md) and [`docs/adr/`](../adr/README.md). See [`master-plan.md`](master-plan.md) §3, §6.5 and §10 for the reasoning this brief compresses.

# GroceryEasy — Phase 3 agent brief

**Scope:** the new single-page application — build tooling, design system, generated API client, authentication, and catalogue and account screens up to parity with the legacy app.
**Budget:** about 36 hours, weeks 6 to 8.
**Branch:** `feat/phase-3-spa-foundation`.
**Depends on:** Phase 2 complete. The OpenAPI document must describe the catalogue and authentication endpoints before the client can be generated from it.

---

## 0. Verified starting state

`frontend/` **does not exist**. There is no `package.json`, no lockfile and no Node tooling anywhere outside `legacy-node/`. `.gitignore` already anticipates the new app with entries for `frontend/web/node_modules`, `frontend/web/dist`, `frontend/web/.vite`, `frontend/web/playwright-report` and `frontend/web/test-results` — so the directory is `frontend/web/`, not `frontend/`. Node on this machine is v20.19.4.

The legacy Create React App frontend stays at `legacy-node/frontend/` and is **read-only**. Do not import from it, do not copy components out of it, and do not run its build. Use it only as a checklist of screens to reach parity with — `legacy-node/frontend/src/App.js` is the complete route inventory.

---

## 1. Hard constraints

1. **`legacy-node/` is read-only,** as in every phase.
2. **The public URL still points at the legacy app.** Phase 3 ships to a `next.` subdomain or to nothing at all. Cutover happens at the end of **Phase 4**, and `render.yaml` and `Procfile` are deleted then, not now.
3. **TypeScript `strict` is on from the first commit,** along with `noUncheckedIndexedAccess`. Retrofitting strict mode is the same trap as retrofitting `TreatWarningsAsErrors`.
4. **No hand-written types for API requests or responses.** Every one is generated from the OpenAPI document. A hand-written interface that drifts from the server is exactly the class of bug that produced `[L-17]`.
5. **The access token lives in memory only.** Never `localStorage`, never `sessionStorage`, never a cookie the JavaScript reads `[L-10]`. The refresh token is an `HttpOnly` cookie the frontend can neither read nor set.
6. **Money is never computed on the client for anything the server will act on.** The cart may display a total; the server recomputes it. This is the frontend half of `[L-01]`.
7. **Conventional Commits, no `Co-Authored-By` trailer, work on a branch, merge by pull request.**
8. **Decisions get the usual three artifacts.** `docs/concepts/frontend-stack-choice.md` and `docs/concepts/api-client-codegen.md` already exist — read them first and correct them if the implementation diverges.

### Non-goals for Phase 3

Do not build: the cart, checkout, slot picker or order screens (Phase 4) · payment user interface (Phase 5) · the admin console (Phase 5) · Playwright end-to-end tests (Phase 6) · a production Dockerfile for the web app (Phase 6) · server-side rendering of any kind, now or later.

---

## 2. Task breakdown

### Task 3.1 — Scaffold the application (about 3 hours)

`frontend/web/`, created with Vite, React 19 and the TypeScript template. Package manager: **pnpm**, and commit `pnpm-lock.yaml`.

`tsconfig.json` with `strict: true`, `noUncheckedIndexedAccess: true`, `noUnusedLocals`, `noUnusedParameters`, and path aliases (`@/` to `src/`). ESLint with the TypeScript and React Hooks plugins; Prettier for formatting. `.env.example` in `frontend/web/` carrying `VITE_API_BASE_URL` — the legacy app hardcoded a local network proxy address `192.168.179.1`, which is why this is a configuration value and not a constant.

**Acceptance:** `pnpm install && pnpm build && pnpm lint && pnpm exec tsc --noEmit` all succeed from a clean checkout.

---

### Task 3.2 — Design system (about 4 hours)

Tailwind CSS v4 and shadcn/ui. Set up the theme tokens once — colour, spacing, radius, typography — and build every screen from them. The legacy app shipped Material UI v4 and v5 side by side, which is what happens when a design system is chosen twice.

Build the shared shell first: header with search and cart badge, category navigation, footer, page container, skeleton loaders, empty states, and an error boundary. Screens are much faster once these exist.

**Acceptance:** a component gallery route renders every shared primitive, and the layout is usable at 360 pixels wide. This is an Indian grocery app; most traffic is mobile.

---

### Task 3.3 — The generated API client and its staleness check (about 5 hours)

This is the highest-signal task in the phase. Read [`docs/concepts/api-client-codegen.md`](../concepts/api-client-codegen.md).

- Redux Toolkit with RTK Query as the data layer.
- `@rtk-query/codegen-openapi` generates hooks and types from the API's OpenAPI document into `src/api/generated/`, which is **committed**.
- A `pnpm codegen` script regenerates it.
- **Continuous integration fails if the committed client is stale.** The workflow runs the API, regenerates, and then runs `git diff --exit-code`. That single step is what makes end-to-end type safety real rather than aspirational: a C# handler signature change breaks the TypeScript build.

The practical difficulty is getting the OpenAPI document in continuous integration. Two workable options — pick one and say which in the report:

1. Start the API in the workflow against a PostgreSQL service container and fetch `/openapi/v1.json`.
2. Add a step to the backend workflow that writes the OpenAPI document to a committed file, and generate the client from that file.

Option 2 is simpler and does not need a database in the frontend job. Prefer it unless something makes it impossible.

**Acceptance:** changing a response property name in a C# endpoint and running `pnpm codegen` produces a diff; committing the C# change without the regenerated client makes continuous integration fail.

---

### Task 3.4 — Authentication (about 5 hours)

Read [`docs/concepts/jwt-refresh-token-strategy.md`](../concepts/jwt-refresh-token-strategy.md) — the server side was built in Phase 1 and this is its other half.

- An auth slice holding the access token **in memory only**, plus the current user.
- `baseQueryWithReauth` wrapping RTK Query's base query: on a 401, call `POST /api/auth/refresh` once, then replay the original request. **Queue concurrent 401s behind a single refresh call** — without that, five parallel requests trigger five rotations, four of which present an already-rotated token and trip the server's reuse detection, logging the user out. This is the single most common bug in this pattern; write the test for it.
- On application start, attempt a silent refresh so a returning user with a live refresh cookie is logged in without a prompt.
- Route guards: an authenticated route wrapper and a role-aware one, both of which are **user-experience conveniences only**. The server enforces authorization `[L-06]`. Never let a client-side guard be the only thing protecting anything.
- `POST` for logout, never `GET` `[L-10]`.

**Acceptance:** a Vitest test asserts that three simultaneous 401 responses produce exactly one refresh call. Manually: log in, wait past the 15-minute access token lifetime, act, and stay logged in.

---

### Task 3.5 — The ProblemDetails normalizer (about 2 hours)

One function converting an RFC 9457 ProblemDetails body into a shape the user interface renders: a title, a message, and per-field errors keyed for react-hook-form. Every error message in the application goes through it.

This closes the frontend half of `[L-17]`, where the server returned `{success, error}` and every client read `.message`, so **every error message in the legacy app displayed the word `undefined`**. Handle the cases that actually occur: a validation ProblemDetails with a populated `errors` member, a plain ProblemDetails, a network failure with no response at all, and a non-JSON response such as an HTML error page from a proxy.

**Acceptance:** Vitest covers all four cases. No component anywhere reads `error.response.data.message`.

---

### Task 3.6 — Screens (about 12 hours)

Parity target is `legacy-node/frontend/src/App.js`, minus the cart and order routes that belong to Phase 4.

| Screen | Notes |
|---|---|
| Home | featured categories, promotional strip, the store picker |
| Category browse | facets, sort, keyset pagination — an infinite-scroll or explicit "load more" control, not page numbers |
| Search results | the same list component, driven by the query parameter, with a debounced input |
| Product detail | variant and unit picker; for a loose variant a stepper in `step_qty` increments showing "250 g / 500 g / 1 kg" instead of "1 / 2 / 3" |
| Register, log in, verify email, forgot password, reset password | react-hook-form with zod schemas |
| Profile | name, phone, email verification state |
| Addresses | create, read, update and delete, with `pincode` and `phone` as text inputs, never numeric — the audit records both typed as `Number` in the legacy schema |
| Not found and error | real screens, not a blank page. The legacy order-detail screen rendered the literal string `"OrderDetails Nahi ho raha error solve"` `[L-20]` |

The loose-goods stepper is the screen detail that shows this is a grocery application rather than a generic shop. Give it real attention.

**Acceptance:** every screen renders correctly at 360 pixels wide, has a loading skeleton and an empty state, and shows a normalized message on error.

---

### Task 3.7 — Tests (about 3 hours)

Vitest with React Testing Library and Mock Service Worker. The target is roughly 40 percent coverage, concentrated where it matters rather than spread evenly:

- The ProblemDetails normalizer, all four cases.
- The refresh queue, from Task 3.4.
- Loose-quantity stepping and the display of a price per unit.
- The auth flow through the reducers.

Do not test that a component renders a heading. That is coverage theatre.

**Acceptance:** `pnpm test` green, and the four areas above are covered.

---

### Task 3.8 — Continuous integration for the web application (about 2 hours)

Extend `.github/workflows/ci.yml` with a `web` job: pnpm install with a cache, `tsc --noEmit`, ESLint, Vitest, `vite build`, and the codegen staleness check from Task 3.3.

**Acceptance:** the pull request shows both the backend job and the web job green, and a deliberately stale generated client turns the job red.

---

## 3. Definition of done

1. `pnpm install && pnpm exec tsc --noEmit && pnpm lint && pnpm test && pnpm build` all succeed from a clean checkout.
2. `pnpm codegen && git diff --exit-code` is clean, and continuous integration enforces it.
3. Browse, search, register, verify, log in, and then stay logged in past the access-token lifetime through a silent refresh, without re-entering a password.
4. Three simultaneous 401 responses produce exactly one refresh call, proved by a test.
5. Every screen in Task 3.6 exists and is usable at 360 pixels wide.
6. No component reads `error.response.data.message`; every message goes through the normalizer.
7. The access token appears nowhere in `localStorage` or `sessionStorage` — check the browser storage panel and say so in the report.
8. The legacy application is still the public URL and still runs.
9. Continuous integration green on the pull request, both jobs.
10. `git diff v1-legacy-node -- legacy-node/` returns empty.

---

## 4. Known pitfalls

- **The refresh stampede.** Covered in Task 3.4 and worth repeating: without a single-flight queue, parallel 401s trip the server's reuse detection and log the user out. It looks like a server bug and is not.
- **Cookies and origins.** The refresh cookie is `SameSite=Strict` scoped to `/api/auth`. During development the Vite server on port 5173 and the API on a different port are different origins, so plan for a Vite proxy rather than fighting cross-origin cookie rules.
- **`Path=/api/auth`** means the cookie is only sent to that path. If the proxy rewrites paths, the cookie silently stops being attached.
- **Keyset pagination is not page numbers.** The list component must carry a cursor, not an index. Designing the component around page numbers first and converting later is a rewrite.
- **shadcn/ui is copied into the repository, not installed.** Its components are yours to edit and are committed; do not expect upgrades to arrive through the package manager.
- **Tailwind v4 configures differently from v3** — mostly through CSS rather than `tailwind.config.js`. Follow v4 documentation, not older tutorials.
- **`VITE_` prefix.** Only variables prefixed `VITE_` reach client code. A missing prefix produces `undefined` at runtime with no build error.

---

## 5. Reporting back

Same protocol as the earlier phases: every definition-of-done item proved by command output or a stated manual check, every deviation named and justified, and the branch and commit range. State explicitly which of the two OpenAPI-in-continuous-integration options from Task 3.3 was chosen and why.
