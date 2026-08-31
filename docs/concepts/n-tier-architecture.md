# N-tier (layered) architecture

## What it is

The classic, decades-old default: split the application into horizontal layers, each depending on the one below it.

```
Presentation  (controllers, views)
     ↓
Business Logic  (services)
     ↓
Data Access  (repositories, DB calls)
     ↓
Database
```

The defining trait, and the thing that separates it from [Clean/Onion Architecture](clean-architecture.md), is **which way the dependency arrow points**: straight down, toward the database. `Business` depends directly on `DataAccess`, which depends directly on the actual database technology. Nothing inverts that relationship with an interface.

## Where else you'll see it

The overwhelming majority of tutorial-grade and older enterprise CRUD apps: a typical ASP.NET MVC + `IProductService` + EF repository codebase, most Spring MVC apps, and — relevantly here — the original MERN stack: `routes/ → controllers/ → models/`, Express and Mongoose playing the role of Business Logic and Data Access with no layer boundary enforced by anything but convention.

## Why it's a real risk, not just an aesthetic complaint

Because nothing *prevents* business logic from ending up in the wrong layer, it tends to end up wherever is most convenient at the time — usually the controller, since that's where the request data already is. There's no compiler error for putting a pricing calculation directly in an HTTP handler; it just works, until someone hits a different route that skips it.

## In GroceryEasy

Explicitly considered and rejected — see `C1` in [`docs/engineering-decisions.md`](../engineering-decisions.md). The legacy MERN app is the worked example of what goes wrong: pricing and stock rules lived directly in the order controller (`L-01`, `L-03` in [`docs/legacy-audit.md`](../legacy-audit.md)), so any route that bypassed that controller bypassed the business rule entirely, and nothing at compile time could have caught it.

## Interview questions

**Q: What's actually wrong with N-tier — it's simple and everyone knows it?**
Nothing structural stops business rules from leaking into whichever layer is most convenient at the time (usually the controller), because the dependency arrow points down toward the database rather than the interfaces being owned by the business layer. It works fine under discipline; it degrades silently under time pressure and turnover.

**Q: Give a concrete failure mode.**
A pricing rule implemented inside one controller action. A second endpoint that also needs to create the same kind of record — an admin bulk-import route, say — doesn't go through that controller, so the rule silently doesn't apply there. Nothing fails loudly; the data is just wrong.

**Q: Is N-tier ever the right choice?**
For small, short-lived, low-stakes CRUD tools where the team is small and the code will be rewritten before the discipline erodes, it's a reasonable, cheap default. The tradeoff changes once the cost of a leaked business rule (money, safety, compliance) is high.
