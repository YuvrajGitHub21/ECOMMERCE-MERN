# Concepts and interview primers

A short explainer for every pattern or technology GroceryEasy uses — or seriously considered and rejected. Each doc teaches the *general* concept first, independent of this project, then maps it onto the actual decision, then closes with the questions an interviewer is likely to ask about it.

This folder is a study aid, not a decision record. For what GroceryEasy actually chose and why, [`docs/engineering-decisions.md`](../engineering-decisions.md) and [`docs/adr/`](../adr/README.md) remain authoritative — if this folder and those ever disagree, those win.

**Every significant decision gets one of these.** New file → [TEMPLATE.md](TEMPLATE.md). The bar for "significant enough": expensive to reverse, constrains later choices, or would surprise a competent developer reading the code (same test as [ADR-0000](../adr/0000-record-architecture-decisions.md)).

The sections below mirror `engineering-decisions.md`'s lettered sections (A–I) one for one.

## A · Platform & language

| Doc | Question it answers |
|---|---|
| [language-platform-choice.md](language-platform-choice.md) | Why C#/.NET, and what's the technical case against Node, Python, Rust, Go, and Java? |
| [dotnet-version-choice.md](dotnet-version-choice.md) | Why .NET 10 (LTS) specifically, and what does LTS vs. STS actually mean? |
| [database-choice.md](database-choice.md) | Why PostgreSQL over MongoDB, MySQL, or DynamoDB? |

## B · Migration strategy

| Doc | Question it answers |
|---|---|
| [migration-strategy.md](migration-strategy.md) | What is the Strangler Fig pattern, and why was a clean-room rewrite chosen instead? |
| [technical-debt-triage.md](technical-debt-triage.md) | How do you decide whether to fix legacy bugs or freeze and replace the code? |

## C · Application architecture

| Doc | Question it answers |
|---|---|
| [clean-architecture.md](clean-architecture.md) | What is Clean Architecture, and why does GroceryEasy use it? |
| [onion-architecture.md](onion-architecture.md) | What is Onion Architecture, and how is it different from Clean Architecture? |
| [n-tier-architecture.md](n-tier-architecture.md) | What is N-tier / layered architecture, and why was it rejected here? |
| [vertical-slice-architecture.md](vertical-slice-architecture.md) | What is Vertical Slice Architecture, and how does it combine with Clean Architecture? |
| [minimal-apis.md](minimal-apis.md) | What is the Minimal API pattern? |
| [mvc-controllers.md](mvc-controllers.md) | What is the MVC controller pattern, and when is it still the right call? |
| [cqrs.md](cqrs.md) | What is CQRS — and is it the same thing as MediatR? |
| [mediatr.md](mediatr.md) | What is MediatR, and why does GroceryEasy hand-roll it instead of using the library? |
| [object-mapping-strategy.md](object-mapping-strategy.md) | Why manual mapping and EF Core projection over AutoMapper or Mapster? |
| [result-pattern.md](result-pattern.md) | What is the Result pattern, and when should it replace exceptions? |
| [repository-pattern.md](repository-pattern.md) | What is the repository pattern, and why not a generic `IRepository<T>`? |

## D · Correctness

| Doc | Question it answers |
|---|---|
| [server-authoritative-pricing.md](server-authoritative-pricing.md) | Why does the order command carry no money fields at all, instead of validating client-supplied totals? |
| [inventory-concurrency-strategies.md](inventory-concurrency-strategies.md) | `SELECT FOR UPDATE`, optimistic concurrency, or a conditional `UPDATE` — how do you actually stop overselling stock? |
| [idempotency-keys.md](idempotency-keys.md) | What is the idempotency-key pattern, and why generate the key once at checkout-mount, not per click? |
| [transactional-outbox.md](transactional-outbox.md) | What is the Transactional Outbox pattern, and when do you actually need it? |
| [state-machine-pattern.md](state-machine-pattern.md) | Why model order status as an explicit state machine instead of a plain enum field? |
| [cart-storage-strategy.md](cart-storage-strategy.md) | Why does the cart live in Postgres, not Redis or `localStorage`? |

## E · Security and authentication

| Doc | Question it answers |
|---|---|
| [authentication-framework-choice.md](authentication-framework-choice.md) | Why ASP.NET Core Identity over hand-rolled auth or an external IdP like Auth0? |
| [jwt-refresh-token-strategy.md](jwt-refresh-token-strategy.md) | `localStorage`, an `HttpOnly` cookie, or in-memory + rotating refresh — which actually stops XSS and CSRF? |
| [authorization-strategy.md](authorization-strategy.md) | What is resource-based authorization, and why is it safer than route middleware alone? |

## F · Infrastructure

| Doc | Question it answers |
|---|---|
| [search-strategy.md](search-strategy.md) | Why PostgreSQL full-text search over Elasticsearch or Meilisearch? |
| [image-storage-strategy.md](image-storage-strategy.md) | Why object storage with presigned uploads over base64-in-database? |
| [background-jobs-strategy.md](background-jobs-strategy.md) | Why Hangfire over a bare `IHostedService` or Quartz.NET? |
| [caching-strategy.md](caching-strategy.md) | What is `HybridCache`, and why does it beat raw `IDistributedCache`? |
| [multi-tenancy-strategy.md](multi-tenancy-strategy.md) | Single database with a discriminator, schema-per-tenant, or database-per-tenant? |
| [database-migration-deployment.md](database-migration-deployment.md) | Why apply migrations at release instead of calling `Database.Migrate()` at startup? |

## G · Testing

| Doc | Question it answers |
|---|---|
| [integration-testing-strategy.md](integration-testing-strategy.md) | Why Testcontainers over the EF Core in-memory provider or SQLite? |
| [architecture-testing.md](architecture-testing.md) | What are "fitness functions," and why enforce architecture rules as executable tests? |
| [xunit-vs-nunit.md](xunit-vs-nunit.md) | What is xUnit v3, how does it compare to NUnit, and why xUnit here? |

## H · Frontend

| Doc | Question it answers |
|---|---|
| [frontend-stack-choice.md](frontend-stack-choice.md) | Why Vite + TypeScript + RTK Query over Create React App or Next.js? |
| [api-client-codegen.md](api-client-codegen.md) | Why generate the TypeScript client from OpenAPI instead of hand-writing types? |

## I · Payments and hosting

| Doc | Question it answers |
|---|---|
| [payment-gateway-choice.md](payment-gateway-choice.md) | Why Razorpay + Cash on Delivery over Stripe? |
| [hosting-strategy.md](hosting-strategy.md) | Why compose several free tiers instead of one all-in-one host? |

## Related

- [`docs/engineering-decisions.md`](../engineering-decisions.md) — the narrative record of what GroceryEasy actually chose, with the alternatives and the cost of each.
- [`docs/adr/`](../adr/README.md) — the formal, immutable decision record.
- [`docs/legacy-audit.md`](../legacy-audit.md) — the 20 defects in the pre-rewrite MERN app that motivate several of these choices.
