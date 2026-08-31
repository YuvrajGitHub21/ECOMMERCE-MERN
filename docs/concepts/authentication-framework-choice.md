# Authentication framework choice: ASP.NET Core Identity vs. the alternatives

## The criteria that actually matter for this decision

Not "which auth library is popular" — the properties that matter for *this* project's actual failure history and constraints:

- **Credential storage must be impossible to get subtly wrong.** The legacy app didn't fail because bcrypt is weak — it failed because a hand-written hook around a correct library call had a one-token bug (`L-08`). The criterion isn't "uses a strong hash function," it's "the hashing code path has no room for a developer to reintroduce that class of bug."
- **Session/credential invalidation has to be a first-class concept, not an afterthought.** The legacy app had no way to invalidate a token short of waiting for it to expire — deleting a user or demoting an admin did nothing to tokens already issued (`L-07`).
- **Brute-force and enumeration defenses need to exist by default, not be remembered per-endpoint.** Login had no rate limit, no lockout, and forgot-password leaked which emails were registered (`L-11`).
- **The interesting engineering work for a portfolio project is the token layer, not credential storage.** Password hashing is a solved, boring, high-consequence-if-wrong problem — the kind of thing you want off your plate so effort goes toward the part that actually differentiates competence (E2).
- **Cost and control matter at this project's scale**, even though it's a portfolio app: a demo with unpredictable, near-zero traffic is exactly the shape external identity platforms price against (per-MAU billing), and self-hosting removes a dependency on a third party staying up during a live interview screen-share.

## Comparison at a glance

| Criterion | Hand-rolled (bcrypt + custom tables) | ASP.NET Core Identity (chosen) | External IdP (Auth0 / Entra ID) |
|---|---|---|---|
| Password hashing | Whatever you wire up, however you wire it up | `PasswordHasher<TUser>` — PBKDF2-HMAC-SHA256, 100k+ iterations, versioned format with automatic rehash-on-verify | Not your problem at all — fully outsourced |
| Account lockout | Must be built from scratch | Built in (`MaxFailedAccessAttempts`, `DefaultLockoutTimeSpan`) | Built in |
| Token/credential invalidation | Must be designed and built from scratch | `SecurityStamp`, validated per request; changes on password/role change | Built in (session revocation via provider) |
| Email confirmation / password reset tokens | Must be built and kept constant-time-safe | `DataProtectorTokenProvider`, time-limited, purpose-bound | Built in |
| SSO / social login | Not applicable | Not built in — would need external OIDC middleware on top | Native, out of the box |
| Compliance certifications (SOC 2, etc.) | None | None — inherits whatever the host provides | Provider typically holds these |
| Cost at scale | Free (your infra cost only) | Free (your infra cost only) | Per-MAU pricing, can dominate infra cost at scale |
| Data residency / control | Full control | Full control | Credentials and session data live with a third party |
| What it demonstrates in an interview | Attempting the hard part, badly | Correct use of a vetted framework component — the demonstrable skill | Wiring an SDK — the interesting part is outsourced |

## Why not each alternative — the technical case

### Hand-rolled users and password hashing

The most tempting alternative because it looks like less machinery, and it's what the legacy app did.

- Password hashing itself (`bcrypt.hash`) is not the risk — the risk is everything *around* it: the pre-save hook, the conditional re-hash-only-if-changed logic, the fallback when a field is loaded without the password column. The legacy bug wasn't "chose a weak algorithm," it was a **missing `return`** in a Mongoose `pre("save")` hook: `if (!this.isModified("password")) { next(); }` falls through instead of returning, so the hash runs unconditionally on every save, including saves that never touched the password. On the `forgotPassword` path the document was loaded without the `password` field selected, so `bcrypt.hash(undefined, 10)` was called — which rejected, which (per the same swallowed-rejection bug elsewhere) reached an `unhandledRejection` handler that called `process.exit(1)`. One missing keyword bricked accounts and could crash the process. See [`L-08`](../legacy-audit.md#l-08).
- This is the general shape of the risk with hand-rolled auth: the primitive (a hash function) is easy to get right; the *surrounding state machine* (when to hash, when not to, what "changed" means, what to do when a field is absent) is where bugs live, and there is no framework whose job it is to have already made those decisions correctly.
- Every other piece — lockout counters, security-stamp-equivalent invalidation, token providers for email confirmation and password reset with correct expiry and single-use semantics — would also need to be designed and built, each one a fresh opportunity for the same category of subtle logic bug.
- **Where it would have won:** genuine minimalism for a throwaway prototype with no real users, or a case with truly unusual credential requirements Identity's extension points can't express. Neither applies here.

### ASP.NET Core Identity — what it actually provides (not just "vetted")

Concretely, so "vetted" is backed by specifics rather than asserted:

- **`PasswordHasher<TUser>`** — PBKDF2 with HMAC-SHA256, a configurable iteration count in the tens of thousands (v3 format, the default since ASP.NET Core 3.0), a random per-password salt, and a versioned hash format so the algorithm can be upgraded later without breaking existing hashes — `VerifyHashedPassword` detects an outdated format and Identity can transparently rehash on next successful login. This is functionally the same tier of algorithm as bcrypt (both are deliberately slow, salted, tunable-cost KDFs) — the point isn't that PBKDF2 beats bcrypt, it's that the iteration count, salting, and versioning are all handled by code that has been through years of public scrutiny, instead of assembled by hand.
- **Account lockout** — `MaxFailedAccessAttempts` and `DefaultLockoutTimeSpan` on `IdentityOptions`, enforced automatically by `SignInManager.CheckPasswordSignInAsync`, with a `LockoutEnd` timestamp on the user row. No hand-written failure counter to get wrong.
- **`SecurityStamp`** — a value on the user row that changes whenever the password or security-relevant profile data changes, and is embedded in issued cookies/tokens as a validation check. This is the direct structural fix for `L-07`: the legacy app's JWTs stayed valid until expiry no matter what happened to the account afterward, including deletion. With a security-stamp-style check wired into the validation pipeline, changing or invalidating the credential invalidates already-issued sessions without needing a revocation list.
- **Token providers** (`DataProtectorTokenProvider<TUser>` and friends) — purpose-bound, time-limited tokens for email confirmation and password reset, generated via ASP.NET Core's Data Protection stack rather than a hand-rolled random string with hand-rolled expiry logic. This is what backs the reset flow that never worked in the legacy app (`L-09`) and the enumeration bug in forgot-password (`L-11` — Identity's flow is designed around returning the same response whether or not the account exists, since the token issuance step doesn't require confirming existence to the caller).

### An external provider (Auth0, Microsoft Entra ID, other OIDC IdPs)

The most credible "why wouldn't you just use the managed thing" alternative, and worth being honest about both directions of the trade.

- **The portfolio-specific reason, stated as such:** this project's job is to demonstrate engineering ability, and authentication design — credential storage, token lifetime, revocation, rotation — is one of the areas interviewers probe hardest. Outsourcing it to Auth0 answers "how do you handle auth?" with "I configured a third-party SDK," which is a legitimate integration skill but not the skill this project exists to show. That's an honest, portfolio-specific reason, not a universal engineering argument — a real company evaluating the same choice for a production system would weigh it differently.
- **The general engineering reasons a real company might still choose self-hosted Identity over an external IdP, independent of the portfolio angle:**
  - **Cost at scale.** External IdPs bill per monthly active user; for a consumer app with a large or unpredictable user base, that cost line grows with success in a way self-hosted auth's infrastructure cost does not. A company optimizing for margin at high user counts has a real reason to own this layer.
  - **Data residency and control.** Credentials and session data live with a third party by default. Some regulatory or contractual contexts require keeping that data in-house or in a specific jurisdiction, which self-hosting satisfies trivially and an external IdP requires a specific (and sometimes unavailable) configuration to satisfy.
  - **No dependency on a third party's uptime for a core user-facing flow.** If Auth0 has an incident, every app relying on it for login has an incident too. Self-hosted auth fails only when your own infrastructure fails.
- **What you give up by not using one — stated honestly:** SSO and social login (Google/GitHub/etc.) come essentially free with an external IdP and require real work to bolt onto Identity (adding OIDC/OAuth external-login middleware, which Identity supports but doesn't provide out of the box); compliance certifications (SOC 2, ISO 27001) that an enterprise buyer might require are the provider's to hold, not yours to earn; and the ongoing maintenance burden — patching, monitoring, keeping the auth stack current — is fully yours with self-hosted Identity and fully the provider's with an external IdP.
- **Where it would clearly win:** a B2B SaaS product where enterprise customers require SSO/SAML on day one, or any product where "we don't want to be in the business of storing credentials" is a genuine risk-reduction decision management wants to make. Neither applies to a single-tenant-feeling demo app being built to show auth design skill.

## The non-technical factor — stated separately

The choice to build the token layer by hand (E2) rather than lean further on a higher-level auth-as-a-service platform is partly a portfolio decision: auth design is disproportionately likely to come up in a technical interview for the kind of role this project targets, so keeping it in-house and explainable is worth more here than it would be for a team shipping a product where auth is a solved problem they'd rather not spend engineering time re-solving. This is stated separately from the technical case above on purpose — the technical case for Identity over hand-rolled hashing stands on its own regardless of audience; only the Identity-vs-external-IdP call has a portfolio-specific tilt mixed into it.

## In GroceryEasy

See `E1` in [`docs/engineering-decisions.md`](../engineering-decisions.md) for the formal record. Identity owns credential storage, hashing, lockout, security stamps, and token providers; the JWT/refresh-token layer built on top is documented separately in [jwt-refresh-token-strategy.md](jwt-refresh-token-strategy.md) (`E2`), and authorization is documented in [authorization-strategy.md](authorization-strategy.md) (`E3`). Closes [`L-08`](../legacy-audit.md#l-08) (password re-hashed on every save), [`L-07`](../legacy-audit.md#l-07) (deleted user's token still authenticates), [`L-09`](../legacy-audit.md#l-09) (broken, host-header-poisonable reset link), and [`L-11`](../legacy-audit.md#l-11) (no lockout, forgot-password enumerates users).

## Interview questions

**Q: Why not just use Auth0 or a similar managed identity provider — wouldn't that be less work and more secure?**
Two separate arguments, and it's worth being honest about both. Practically, it probably would be less work. But this project exists partly to demonstrate auth design skill, and outsourcing the interesting part answers "how do you handle authentication" with "I configured an SDK." Independent of that portfolio angle, there are real engineering reasons a company might still choose self-hosted Identity: no per-MAU billing at scale, full control over where credential data lives, and no dependency on a third party's uptime for login. What you give up is SSO/social login out of the box and someone else holding your compliance certifications.

**Q: Isn't rolling your own auth always a bad idea — why even consider hand-rolling it?**
It's a bad idea for the parts that are a solved problem with a narrow, well-understood correct answer — password hashing, lockout, token invalidation — which is exactly why Identity handles those. The legacy app's actual bug wasn't a weak hash algorithm, it was a missing `return` in a pre-save hook that re-hashed the password on every save and could brick accounts. That's not a hashing-algorithm failure, it's a state-machine failure around a correct primitive — and it's precisely the kind of bug that doesn't exist if the framework owns that state machine instead of a hand-written hook.

**Q: What does ASP.NET Core Identity actually give you beyond "a password hasher"?**
Four concrete things: `PasswordHasher<TUser>` (PBKDF2-HMAC-SHA256, tens of thousands of iterations, versioned so the algorithm can be upgraded later without breaking existing hashes), built-in account lockout after repeated failures, a `SecurityStamp` on the user row that invalidates issued credentials when the password or profile changes, and purpose-bound, time-limited token providers for email confirmation and password reset. Each of those maps directly onto a bug the legacy app actually had.

**Q: Give a concrete example of a legacy bug this specific decision prevents.**
`L-07`: a deleted user's JWT kept authenticating until it expired, because there was no revocation mechanism — the token was self-contained and nothing checked the account still existed or still had the same role. Identity's `SecurityStamp` is validated on each request and changes whenever password or role changes, so a deleted or demoted account's existing tokens stop being accepted without needing a blocklist.

**Q: If cost and control are real reasons to avoid external IdPs, why doesn't every company self-host auth?**
Because the trade genuinely goes the other way for a lot of companies: SSO/social login is often a hard requirement for enterprise buyers, compliance certifications an IdP already holds can be the difference between closing a deal or not, and the ongoing engineering time spent maintaining a self-hosted auth stack is real and recurring. It's a legitimate build-vs-buy call that depends on user base size, compliance requirements, and whether the team wants to own that maintenance surface — not a case where one side is simply correct.
