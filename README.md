# GroceryEasy

An online storefront for local kirana stores, where customers choose **self-pickup or delivery** at checkout.

> **Status: mid-rewrite (Phase 0 of 6).**
> The original MERN application is frozen in [`legacy-node/`](legacy-node/) at tag [`v1-legacy-node`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/tree/v1-legacy-node). The replacement — an ASP.NET Core (.NET 10) API over PostgreSQL with a Vite/React/TypeScript frontend — starts in Phase 1.
> The full README, architecture diagram and live demo arrive in Phase 6.

---

## The problem

Quick-commerce platforms like Blinkit and Zepto made grocery shopping convenient, while most neighbourhood kirana stores remain entirely offline. GroceryEasy is an attempt to close that gap without replacing the store: the owner manages their own catalogue, pricing and orders, and customers get search, a cart, and — the part that distinguishes a local store from a dark-warehouse chain — **the choice to collect an order themselves or have it delivered.**

## Why it is being rewritten

The original version was built in my second year of college. It worked, and it was deployed.

It also could not survive contact with real money. Before rewriting, I audited the whole thing and documented **20 defects** — forgeable order totals, a payment flow that was a `setTimeout`, an inventory decrement that could terminate the process, and an admin route guard that had never once executed.

**→ [`docs/legacy-audit.md`](docs/legacy-audit.md)** — every defect, with a repro and the design decision in the new system that makes its class of bug unrepresentable.

That document is the most useful thing in this repository right now.

## Architecture decisions

**→ [`docs/engineering-decisions.md`](docs/engineering-decisions.md)** — every significant technical choice, the alternatives weighed against it, what it costs, and the phase-by-phase roadmap. Why Clean Architecture over N-tier or pure vertical slices, why Minimal APIs over MVC controllers, why PostgreSQL full-text search instead of Elasticsearch, and so on.

The formal per-decision record lives in [`docs/adr/`](docs/adr/README.md). The ones that shape everything else:

| | |
|---|---|
| [0001](docs/adr/0001-clean-room-rewrite-over-strangler-fig.md) | Clean-room rewrite over strangler-fig — and why the strangler pattern was the wrong tool here |
| [0002](docs/adr/0002-postgresql-over-mongodb.md) | PostgreSQL over MongoDB — transactions and constraints the document model could not express |
| [0013](docs/adr/0013-target-dotnet-10-lts.md) | .NET 10 LTS, not .NET 9 |

## Running the infrastructure locally

Requires Docker.

```bash
cp .env.example .env
docker compose --profile core up -d
```

Every service belongs to a profile, so the `--profile` flag is required — a bare `docker compose up -d` starts nothing.

| Profile | Service | URL | Notes |
|---|---|---|---|
| `core` | PostgreSQL | `localhost:5432` | `pg_trgm`, `unaccent`, `citext` pre-installed |
| `core` | Redis | `localhost:6379` | |
| `full` | Mailpit | http://localhost:8025 | catches all outbound mail; nothing leaves your machine |
| `full` | MinIO console | http://localhost:9001 | S3-compatible storage, same API as Cloudflare R2 |
| `full` | Aspire dashboard | http://localhost:18888 | OpenTelemetry traces, metrics and logs |

`core` is enough for API and database work. Use `docker compose --profile full up -d` once email verification, image upload or tracing are involved.

`docker compose --profile full down` stops everything; add `-v` to also delete the data volumes.

The API and web app land in Phase 1 and Phase 3 respectively.

## Running the frozen legacy app

Kept runnable as an honest before-picture. It needs a MongoDB connection string in `legacy-node/backend/config/config.env`.

```bash
cd legacy-node
npm install
npm start          # http://localhost:4000
```

`legacy-node/` is read-only. Its defects are documented, not fixed — see [ADR-0001](docs/adr/0001-clean-room-rewrite-over-strangler-fig.md).

## Repository layout

```
backend/            ASP.NET Core solution            (Phase 1)
frontend/web/       Vite + React + TypeScript SPA    (Phase 3)
legacy-node/        the original MERN app — FROZEN
docs/               architecture decisions + the legacy audit
deploy/             container and infrastructure config
```
