# Hosting strategy: composed free tiers vs. the alternatives

## The criteria that actually matter for this decision

Not "how do you host a web app" generically — the properties that matter for a solo, no-revenue
portfolio project that still has to *work* when a stranger clicks the link, possibly months after
it was deployed:

- **No silent expiry.** A demo that quietly stops working after a fixed window is worse than one
  that's honestly always-down, because it fails at the least convenient moment: when someone
  actually clicks the link, with zero warning beforehand.
- **Cold start has to be *managed*, not eliminated at any cost.** A free-tier compute service that
  sleeps after inactivity is a real constraint, not a disqualifier — the question is whether the
  cold start can be hidden or mitigated well enough that a visitor's first impression isn't "this
  is broken."
- **Zero ongoing cost**, because this project produces no revenue to justify one. This isn't a
  taste preference; it's a hard constraint that legitimately eliminates otherwise-reasonable
  options.
- **Each piece of infrastructure has a different actual failure mode under a free tier**, and they
  don't share one. A static SPA's free-tier risk (build minutes, bandwidth caps) is nothing like a
  managed Postgres free tier's risk (storage caps, and often a hard expiry), which is nothing like
  a compute service's risk (sleep after inactivity). Optimizing for one specific platform's
  specific limitation, instead of accepting whatever limitation a single all-in-one platform
  happens to ship with, is the actual engineering content of this decision.

## Comparison at a glance

| Criterion | Composed free tiers (chosen) | Single all-in-one free PaaS | Single paid VPS |
|---|---|---|---|
| Ongoing cost | $0 | $0 | Real, recurring — for a project with no revenue |
| SPA cold start | None — static CDN assets, always instantly available | Shared with the API's cold start if served from the same service | None, but only because *nothing* on a VPS free-tiers into existing |
| Database expiry risk | None (Neon has no forced expiry) | Real — e.g. Render's free Postgres expires after 90 days | None, but the operator now owns backups, patching, and failure recovery entirely |
| API cold start | Present — Render's free web services sleep after inactivity | Present, same root cause | None — always-on, but that's what's being paid for |
| Operational surface owned by the developer | Low per service, several services to know about | Low — one dashboard | High — OS patching, security updates, backups, monitoring, all manual |
| Failure mode if ignored for months | Nothing breaks silently | The demo dies quietly the day the Postgres free tier expires | The VPS keeps running and keeps costing money whether or not anyone ever visits |

## Why not each alternative — the technical case

### A single all-in-one free PaaS hosting everything

This is not a hypothetical — it's literally the legacy app's original deployment shape (Render,
everything on one platform), so the case against it is grounded in what actually happened, not a
guess.

- **Render's free Postgres tier expires after 90 days.** That's not a soft limitation that
  degrades gracefully — the database is deleted and data is gone. For a portfolio project that
  might sit unvisited for a while and then get pulled up in an interview loop, a 90-day clock that
  started on last deploy is a landmine with a delay fuse: it fails **silently**, with no user
  action that triggers it, and the first sign of trouble is exactly the moment someone clicks the
  link expecting it to work.
- The single-platform convenience — one dashboard, one bill (or lack of one), one set of docs —
  is real and shouldn't be dismissed. It's the right tradeoff for a genuinely disposable
  weekend project where "it might die in three months" is an acceptable outcome. It is not
  acceptable for a project whose entire purpose is to be pulled up months later as evidence of
  engineering judgment.
- **Where it would have won:** fastest possible path to *something* live, single point of
  configuration, no cross-service networking/CORS/env-var wiring to get right. For extremely
  short-lived demos (a hackathon judged the same weekend), that's a legitimate reason to prefer it.

### A single paid VPS

The real alternative to a free-tier patchwork — worth engaging with directly rather than waving
away as "costs money."

- **Ongoing cost for a project generating no revenue.** Even a cheap VPS is a recurring bill for
  something whose only "customer" is whoever clicks a link during a job search. That's a real,
  continuing cost with no offsetting return — the kind of tradeoff that's easy to justify for a
  production service and hard to justify for a portfolio artifact.
- **More operational surface to own, for a demo-only workload that doesn't need it.** A VPS means
  being personally responsible for OS security patches, process supervision, TLS certificate
  renewal, backup strategy, and monitoring — none of which the workload (a low-traffic demo)
  actually needs solved at that level. Every one of those is a place to make a mistake that a
  managed free-tier service has already solved once, for everyone, as its actual product.
- **Where it would have won:** genuinely needs to run background processes with no sleep at all,
  needs full control over the runtime environment, or the project's actual goal included
  demonstrating VPS/Linux ops skills. None of those apply here — the goal is demonstrating backend
  and architecture skill, not systems administration.

## The chosen approach: compose several specialized free tiers, matched to what each is best at

The core idea, stated plainly: **rather than accepting whatever limitation one all-in-one
platform happens to have, match each piece of infrastructure to the free-tier limitation that
would actually hurt *this* project's specific use case**, and let a different provider absorb
each one.

- **Cloudflare Pages for the SPA.** Static assets served from a CDN have no cold start by
  construction — there's no server process to sleep, just files at edge locations. The link feels
  instant even if the API behind it is asleep, which matters because first impressions in a demo
  are set by what loads first.
- **Neon for Postgres.** No 90-day forced expiry, which is the exact failure mode that would have
  silently killed the demo under the legacy Render setup. Neon's free tier has its own real
  constraints (storage ceiling, compute-hour limits, autosuspend), but none of them are a data-loss
  time bomb.
- **Upstash for Redis.** Used here only for a derived cache (the cart header-badge summary, `D6`
  in [`engineering-decisions.md`](../engineering-decisions.md)), which is exactly the right role
  for a free-tier Redis whose eviction behavior can't be fully trusted as durable storage.
- **Cloudflare R2 for images.** Zero egress fees matters specifically because a demo link can get
  hammered unpredictably (shared in a group chat, scraped, crawled) with no revenue to absorb a
  surprise bandwidth bill — the one free-tier risk that would actually cost real money if ignored.
- **Render's free Docker web service for the API only** — not for everything, just the one
  component that actually needs to run arbitrary server code continuously.

### Why the API specifically still has a cold-start problem, and what mitigates it

Render's free web services **sleep after a period of inactivity** and take on the order of tens of
seconds to spin back up on the next request — this is the one piece of the composition that
*doesn't* dodge the cold-start problem, because it's the one piece that's genuine server
compute rather than static assets or a managed data store.

Two mitigations, stacked rather than relying on either alone:

1. **A scheduled cron ping** hits a lightweight health endpoint on an interval short enough to
   keep the instance from crossing Render's inactivity threshold during normal daytime hours —
   reducing how often a real visitor hits a cold instance at all.
2. **The SPA shows an explicit "waking the demo server" loading state** on the first API call
   rather than a bare spinner or, worse, a silent hang. This reframes an unavoidable free-tier
   limitation as *legible, expected loading behavior* instead of *something that looks broken* —
   the same instinct as the Neon/Render database choice: the constraint can't always be
   eliminated, but it can be kept from being silent.

### The general lesson

The single all-in-one platform makes one implicit choice per resource type — whatever its free
tier happens to be optimized for — and inherits every one of those choices' worst axis, whether or
not that axis matters for the actual project. Composing providers means picking, deliberately, per
resource: *what is the failure mode of this specific free tier, and does it actually threaten this
project?* A 90-day Postgres expiry threatens a portfolio piece meant to still work in six months;
a Redis eviction doesn't, because Redis here only ever holds a derived cache that's cheap to
recompute. That's the actual judgment being exercised — not "free tiers are good," but "know
which specific limitation of which specific free tier would actually hurt you, and route around
that one."

## The non-technical factor — stated separately

Zero ongoing cost is a real constraint here, but it's explicitly a **budget** reason, not a
technical one — a paid VPS or a paid PaaS tier would remove the cold-start and expiry problems
outright, at a real dollar cost that this project has no revenue to justify. Composing free tiers
is the answer to "how do I get all the guarantees I need at zero cost," not a claim that it's
technically superior to paying for a single well-run platform. Kept separate on purpose: the
technical case above (matching failure modes to providers) stands on its own even in a world where
budget wasn't a constraint at all — it's still better engineering to know precisely which limit
you're routing around, whether or not the routing itself is motivated by cost.

## In GroceryEasy

See `I2` in [`engineering-decisions.md`](../engineering-decisions.md) for the formal record. The
Azure Container Apps target with committed Bicep remains the documented production aspiration if
budget ever allows — noted there as the strongest platform signal for a C# employer, separate from
this free-tier demo composition. This decision doesn't map to a single numbered legacy defect the
way the correctness-focused docs do, but it's a direct response to the operational fragility the
legacy app's original Render deployment demonstrated in practice: an all-in-one free platform with
a database that quietly expires.

## Interview questions

**Q: Why not just deploy everything to one free platform, like Render or Railway, and keep it simple?**
That's literally what the legacy app did, and it has a specific, dangerous failure mode: Render's
free Postgres tier expires after 90 days, deleting the database. That's not a gradual degradation
— it's silent, and it fires exactly when someone clicks the demo link months later, expecting it
to work. Composing providers costs a bit of setup complexity in exchange for removing that one
failure mode along with the cold-start-everywhere problem a single service would inherit.

**Q: Doesn't spreading across five providers just multiply your points of failure?**
It multiplies the number of *dashboards*, not meaningfully the number of failure modes that
matter — each provider is handling exactly the piece it's strongest at (a CDN for static assets,
a database provider that doesn't expire databases, object storage with no egress fees), so each
one's free-tier limitation is one this project has already checked doesn't hurt it. A single
platform doesn't remove those limitations, it just hides that you haven't checked which ones
apply to you.

**Q: A paid VPS would remove all of this cold-start and expiry complexity — why not just pay for one?**
It would, and for a revenue-generating service that's usually the right call. This project
generates no revenue, so a recurring bill is a real ongoing cost with nothing offsetting it, and a
VPS also means personally owning OS patching, TLS renewal, and backups for a demo-only workload
that doesn't need any of that solved at that level. It's the right tradeoff to make once this
stops being a portfolio piece and starts being a real product.

**Q: If the SPA has no cold start, why does the API still have one — didn't you solve this?**
No, and it's worth being precise about that rather than overclaiming. Cloudflare Pages serves the
SPA as static files off a CDN, so there's no process to sleep in the first place. The API is
still a real server process on Render's free tier, which sleeps after inactivity — that's an
inherent property of free compute, not something solved by the SPA's hosting choice. What's
actually done is mitigate it: a cron ping to reduce how often it's actually asleep when a visitor
arrives, and an explicit "waking the demo server" loading state so the unavoidable cases read as
loading, not broken.

**Q: What's the concrete failure this design specifically avoids that the legacy setup didn't?**
The legacy deployment ran everything, including Postgres, on Render's free tier — and Render's
free Postgres silently expires after 90 days. If nobody happened to redeploy or notice within that
window, the demo would be dead with zero warning the next time someone opened it. Neon's free
Postgres tier has real constraints of its own (storage, compute-hour limits) but no forced expiry,
so that specific silent-death failure mode is off the table.
