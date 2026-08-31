# Frontend stack choice: Vite + TS + RTK Query vs. the alternatives

## The criteria that actually matter for this decision

GroceryEasy's frontend is a client-heavy, authenticated commerce SPA — carts, checkout, order tracking, an admin console — not a public content site optimizing for search-engine crawlers or first-paint marketing metrics. The criteria that matter for **this specific shape of frontend**, not frontend tooling in general:

- **Dev-loop speed**, since a 14-week solo project spends a large fraction of its time in the edit-save-see-result cycle, and a slow one is a tax paid on every single change.
- **Whether the workload actually benefits from server rendering.** Most of this app sits behind a login: browsing, cart, checkout, order history, admin. SEO and anonymous first-load performance — the exact problems SSR solves — apply to a small, already-static slice (the storefront landing page), not the authenticated bulk of the product.
- **Data-fetching correctness under real-world network conditions** — caching, deduplication, and invalidation handled by a library with a designed cache-consistency model, not five hand-rolled thunks each reinventing loading/error/success state slightly differently.
- **Dependency hygiene**, given the legacy app's own frontend is the worked example of what happens without it — two competing UI kit majors installed and imported simultaneously, `--legacy-peer-deps` required just to install.
- **Operational simplicity of what has to run in production.** The API is already a separate ASP.NET Core service; whether the frontend needs its own Node.js server process at runtime is a real infrastructure question, not a style preference.

## Comparison at a glance

| Criterion | Create React App | Next.js | Chosen: Vite + TS + RTK Query |
|---|---|---|---|
| Maintenance status | Deprecated, unmaintained | Actively maintained | Actively maintained |
| Dev server model | Webpack, full bundle before serving | Webpack/Turbopack, hybrid SSR+bundle | Native ESM — no bundle step in dev |
| Cold start / HMR speed | Slow, grows with app size | Faster than CRA, still a bundler | Fast — esbuild pre-bundles deps only |
| Production build tool | Webpack | Webpack/Turbopack | Rollup |
| Server-side rendering | No | Yes, first-class | No — client-only SPA |
| Needs a Node.js runtime in production | No (static output) | Yes | No (static output) |
| Data fetching | Hand-rolled (Redux thunks, or nothing) | Hand-rolled or React Query, etc. | RTK Query — caching, dedup, invalidation built in |
| Fit for an authenticated, client-heavy SPA | Workable but stagnant | Over-provisioned for this shape | Purpose-fit |

## Why not each alternative — the technical case

### Create React App

- **Deprecated and unmaintained** — the React team itself stopped recommending CRA for new projects; it receives no meaningful updates and carries known, unaddressed issues in its Webpack configuration. Starting a new project on it in 2026 means starting on tooling with no forward path.
- **Webpack-based dev server**, which bundles the application (or a meaningful subset of it) before it can serve anything, and re-bundles affected modules on every change. Compared to esbuild/Rollup-based tooling, this is measurably slower on both cold start and hot-reload for anything beyond a trivial app size — a real, compounding cost across a project with dozens of features and components built incrementally over 14 weeks.
- No credible technical case for choosing it over actively-maintained alternatives in a new project — it's included here only because it's the incumbent the legacy app used, and the "why not just keep using what you had" question is worth a direct answer.

### Next.js

The most credible alternative, and the one most likely to come up as "why didn't you just use the industry-standard React meta-framework."

- **Next.js's core value proposition is server-side rendering and server components** — shipping pre-rendered HTML for fast first paint and search-engine indexability. That trade-off matters most for **public, anonymous, content-heavy pages**: a marketing site, a blog, a storefront a crawler needs to index. GroceryEasy's actual bulk — cart, checkout, order history, the admin console — sits behind authentication, where there's no crawler to serve and no anonymous first-load metric that matters the way it does for a public page; a logged-in user's first meaningful paint after login is dominated by the API round-trip and auth state, not by whether the shell was server-rendered.
- **It adds an entire Node.js server runtime that has to run in production**, on top of an already-separate ASP.NET Core API. That's two runtimes to build, deploy, monitor, and scale (or, for the SSR-serving Node process specifically, to keep warm) where a client-only SPA needs zero — a Vite build's output is static files served from a CDN, with the API doing all the actual work. Given the hosting reality here (`I2`) — a free-tier Render API already fighting cold starts, a Cloudflare Pages SPA specifically chosen because it has none — adding a second server process whose entire job is rendering HTML doesn't remove a cold-start problem, it duplicates it.
- The storefront's public-facing slice (category browse, product pages an unauthenticated visitor might land on from a search result) is the one place Next.js's argument is genuinely strongest — and honestly, if SEO on that specific slice became a real product requirement, revisiting this decision for just that surface would be defensible. It isn't, for a portfolio demo without organic search traffic to capture.
- **Where it would win outright:** a public storefront where anonymous-visitor SEO and first-load performance are the product's actual growth channel — a real grocery delivery startup depending on Google search traffic for customer acquisition, for instance, where the SSR trade-off directly funds the business rather than serving a metric nobody's measuring yet.

## The chosen stack: Vite + TypeScript strict + RTK Query + Tailwind + shadcn/ui

- **Vite** serves source over native ES modules in development — the browser requests modules directly, and Vite doesn't bundle the application at all during dev, only pre-bundling third-party dependencies once via esbuild (written in Go, an order of magnitude faster than a JS-based bundler for that step). The result is a dev server that stays fast regardless of app size, because editing one file only ever triggers that one module's HMR update, not a re-bundle of anything downstream. Production builds switch to **Rollup**, which produces smaller, more optimized static output than a dev-oriented bundler would — dev and prod deliberately use different tools because they're optimizing for different things (iteration speed vs. output size).
- **TypeScript in strict mode** end-to-end, paired with the OpenAPI-generated client (`H2`) — the structural fix for `L-17`, where the legacy app's client and server silently disagreed about the shape of an error response (`{success, error}` on the wire vs. `.message` read on every client call site) and every error toast displayed `undefined`. A generated, typed client makes that specific class of bug a compile error instead of a runtime silent failure.
- **RTK Query** (part of Redux Toolkit) replaces roughly 30 hand-rolled thunks the legacy app used for data fetching — one per async action, each independently implementing its own loading/error/success state, its own retry (or lack of one), its own notion of when cached data goes stale. RTK Query instead defines an endpoint once and gets, for free: **caching** (repeated requests for the same data don't re-fetch), **request deduplication** (two components mounting simultaneously and requesting the same data in the same render cycle collapse into one network call, not two), and **tag-based cache invalidation** (placing an order invalidates the cart query and the order-list query automatically, rather than a developer remembering to manually re-fetch both after every mutation that affects them). This directly targets a bug the legacy app actually shipped: the fire-and-forget `dispatch(createOrder(order))` in `L-15` that navigated to a success screen without ever awaiting the result — a hand-rolled thunk with no designed request-lifecycle contract left "did this actually finish" as a detail each call site had to remember to check, and one didn't.
- **Tailwind + shadcn/ui** for styling and components — utility classes plus accessible, unstyled-by-default component primitives that are copied into the project rather than installed as an opaque dependency, so there's exactly one styling system in the codebase, ever. This directly closes the legacy app's dependency-hygiene failure: `@material-ui/core` v4 (end-of-life, not React 18 compatible) and `@mui/material` v5 were **both installed and both imported simultaneously** — two full component libraries shipping in one bundle, which is the reason the legacy project needed `--legacy-peer-deps` just to `npm install` at all. A project that has already lived through "which component library are we actually using" doesn't get to be ambiguous about it a second time.

## The non-technical factor — stated separately, if one exists

None beyond what's already covered in [language-platform-choice.md](language-platform-choice.md). The choice here is driven entirely by dev-loop speed, the SSR/CSR trade-off actually matching the app's authenticated-first shape, and closing a concrete legacy dependency-hygiene failure — not by cost or hiring-market fit.

## In GroceryEasy

See `H1` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record, alongside `H2` (the generated OpenAPI client, the structural fix for `[L-17]`). This lands in Phase 3 alongside RTK Query wiring and OpenAPI codegen in CI. The dependency-hygiene case maps directly onto a secondary finding in [`docs/legacy-audit.md`](../legacy-audit.md) — both Material UI majors installed and imported at once.

## Interview questions

**Q: Why not Next.js — isn't it the standard choice for a React app in 2026?**
It's a credible default for a lot of apps, but its core value — server-side rendering for SEO and fast anonymous first load — solves a problem this app mostly doesn't have. The bulk of GroceryEasy sits behind authentication: cart, checkout, order history, admin. There's no crawler to serve there and no anonymous-visitor metric that SSR improves. It also means running a Node.js server process in production on top of an already-separate ASP.NET Core API — two runtimes to operate instead of one static SPA build served from a CDN, which works directly against the free-tier hosting setup this project already fights cold starts on.

**Q: Would you actually reconsider Next.js for any part of this app?**
Yes, honestly — the public storefront slice (category browse, product pages an unauthenticated visitor might land on from search) is exactly where SSR's argument is strongest. If organic search traffic became a real acquisition channel for this product, revisiting SSR for just that slice would be a defensible, narrow scope change. It isn't a driver for a portfolio demo with no organic search traffic to capture, which is why the whole app stays a client-only SPA rather than paying that infrastructure cost for a benefit nothing is measuring.

**Q: What does RTK Query actually replace, concretely?**
About 30 hand-rolled Redux thunks in the legacy app, each independently implementing its own loading/error/success state and its own (often absent) handling of staleness and retries. RTK Query defines each data dependency as an endpoint once and gets caching, request deduplication, and tag-based invalidation for free — for example, placing an order invalidates the cart and order-list queries automatically instead of a developer remembering to manually re-fetch both, which is a structural fix for the same class of bug as the legacy app's fire-and-forget `dispatch(createOrder(order))` that navigated to a success screen without ever confirming the order actually succeeded.

**Q: Why does the dependency situation in the legacy frontend matter enough to be a criterion here?**
Because it's a concrete, self-inflicted example of what happens without a stated stack decision: the legacy app had `@material-ui/core` v4 — end-of-life, not even compatible with React 18 — and `@mui/material` v5 installed and imported at the same time, which is why installing the project at all required `--legacy-peer-deps` to bypass npm's own peer-dependency conflict detection. Tailwind + shadcn/ui isn't chosen for aesthetic reasons here so much as to make "which UI system are we using" a question with exactly one answer, permanently.

**Q: Isn't Vite just a faster Webpack — is the difference actually significant?**
The difference isn't degree, it's kind: Webpack (and CRA specifically) has to bundle the application before the dev server can serve anything, and re-bundle affected modules on every change. Vite serves source over native ES modules and only pre-bundles third-party dependencies once, via esbuild — so editing a file triggers an update scoped to that module, not a bundle step that grows with total app size. It matters in practice, not just in benchmarks, because a 14-week project accumulates enough features and components that a bundler-based dev server's startup and HMR time compounds into a real, felt cost on every single edit-save cycle.
