# Legacy audit — the MERN app, before the rewrite

This is an audit of my own code.

GroceryEasy started as a MERN e-commerce app I built in my second year of college. It worked: you could register, browse products, add to a cart, and place an order. It was deployed and it ran.

It also could not survive contact with real money. Before rewriting the backend in .NET, I read the whole thing and wrote down everything wrong with it — 20 defects, plus a set of secondary findings. This document is the result.

The point is not the list. The point is the third column: **for each defect, the design decision in the new system that makes that entire class of bug unrepresentable.** A null check fixes one bug. Removing money fields from the request contract fixes every bug of that shape, forever.

All code links are pinned to the [`v1-legacy-node`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/tree/v1-legacy-node) tag, so they stay valid as the repo changes around them.

---

## Summary

| Class | Count | The single missing idea |
|---|---|---|
| **Input trust** | 2 | The server had no opinion of its own. It persisted whatever the browser sent. |
| **Authorization** | 3 | Guards were written where they were convenient, not where they were enforceable. |
| **Concurrency & integrity** | 3 | Reads and writes were treated as one atomic step. They are not. |
| **Auth & session** | 6 | Tokens were issued but never really validated, revoked, or rate-limited. |
| **Injection** | 2 | User input reached the query engine as syntax rather than as data. |
| **Contract & correctness** | 4 | Client and server disagreed about the shape of a response, and nothing caught it. |

Six of these twenty are the *same* missing idea — *don't trust the client* — appearing in six different places. That is more interesting than the count.

| # | Defect | Severity | Class |
|---|---|---|---|
| [L-01](#l-01) | Order totals and item prices accepted from the client | 🔴 Critical | Input trust |
| [L-02](#l-02) | Payment is a `setTimeout` in the browser | 🔴 Critical | Input trust |
| [L-03](#l-03) | Stock is never checked or reserved when an order is placed | 🔴 Critical | Concurrency |
| [L-04](#l-04) | Stock decrement is unawaited, non-atomic, and can kill the process | 🔴 Critical | Concurrency |
| [L-05](#l-05) | Any logged-in user can delete any review (IDOR) | 🔴 Critical | Authorization |
| [L-06](#l-06) | Every admin route guard on the frontend is inert | 🟠 High | Authorization |
| [L-07](#l-07) | A deleted user's token still authenticates, then 500s | 🟠 High | Auth |
| [L-08](#l-08) | Password is re-hashed on every save; breaks password reset | 🟠 High | Auth |
| [L-09](#l-09) | Reset link is built from the `Host` header and points at the API | 🟠 High | Auth |
| [L-10](#l-10) | Auth cookie lacks `Secure`/`SameSite`; logout is a `GET` (CSRF) | 🟠 High | Auth |
| [L-11](#l-11) | No rate limiting or lockout; forgot-password enumerates users | 🟠 High | Auth |
| [L-12](#l-12) | Unvalidated env vars and a swallowed database failure | 🟠 High | Auth/config |
| [L-13](#l-13) | Regex injection / ReDoS in product search | 🟠 High | Injection |
| [L-14](#l-14) | NoSQL operator injection in the filter chain | 🟠 High | Injection |
| [L-15](#l-15) | Cart is never cleared; no idempotency, so orders duplicate | 🟠 High | Correctness |
| [L-16](#l-16) | Users cannot view their own orders; no ownership check exists | 🟡 Medium | Authorization |
| [L-17](#l-17) | Error contract mismatch — every error toast reads `undefined` | 🟡 Medium | Contract |
| [L-18](#l-18) | Pagination is dead; the whole catalogue ships on every request | 🟡 Medium | Contract |
| [L-19](#l-19) | Editing a review corrupts the rating average to 27 stars | 🟡 Medium | Correctness |
| [L-20](#l-20) | Placeholder components shipped to production | 🟡 Medium | Dead code |

---

## Input trust

### L-01
### Order totals and item prices accepted from the client · 🔴 Critical

**Where:** [`backend/controllers/orderControllers.js:8-35`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/controllers/orderControllers.js#L8-L35)

`newOrder` destructures `itemsPrice`, `taxPrice`, `shippingPrice`, `totalPrice` and `orderItems[]` straight out of `req.body` and writes them to Mongo unchanged. No product is ever loaded. `paidAt` is stamped unconditionally.

```js
const { shippingInfo, orderItems, paymentInfo,
        itemsPrice, taxPrice, shippingPrice, totalPrice } = req.body;

const order = await Order.create({
    shippingInfo, orderItems, paymentInfo,
    itemsPrice, taxPrice, shippingPrice, totalPrice,
    paidAt: Date.now(),          // ← every order is recorded as paid
    user: req.user._id,
});
```

**Exploit** — any authenticated user:

```http
POST /api/v1/order/new
{ "orderItems": [{ "product": "<any real id>", "quantity": 500, "price": 0 }],
  "itemsPrice": 0, "taxPrice": 0, "shippingPrice": 0, "totalPrice": 1,
  "paymentInfo": { "id": "anything", "status": "succeeded" } }
```

→ `201 Created`. A paid order for 500 units at ₹1. The totals shown in the UI were computed in [`ConfirmOrder.js:19-23`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/frontend/src/component/Cart/ConfirmOrder.js#L19-L23) and stashed in `sessionStorage`, which is to say: in the attacker's hands.

**Root cause:** the server has no opinion about price. It is a persistence layer for whatever the browser says.

**Structural prevention:** `PlaceOrderCommand` carries **no money fields at all** — only `cartId`, `fulfillmentType`, `slotId`, `addressId`. Prices are read from `product_variants.sale_price` inside the handler and run through `OrderPricingEngine`, a pure function unit-tested against a rounding and GST-extraction suite. `order_items` stores `unit_price_snapshot` so a later price edit cannot rewrite history. `paid_at` is written **only** by the Razorpay webhook handler. There is no code path that accepts a price from a request body.
→ plan §5.1

---

### L-02
### Payment is a `setTimeout` in the browser · 🔴 Critical

**Where:** [`frontend/src/component/Cart/Payment.js:78-88`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/frontend/src/component/Cart/Payment.js#L78-L88)

```js
setTimeout(() => {
  order.paymentInfo = { id: "mock_payment_id", status: "succeeded" };
  dispatch(createOrder(order));
  navigate("/success");
}, 2000);
```

Card number, expiry and CVC are regex-checked in the browser and then discarded. No money moves. Combined with L-01, an order's price *and* its paid status are both attacker-controlled.

A Stripe controller exists at [`backend/controllers/paymentController.js`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/controllers/paymentController.js), but its route is commented out at [`app.js:34`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/app.js#L34) and the `stripe` package is not in `package.json`. Uncommenting it crashes the server with `MODULE_NOT_FOUND` at require time.

**Structural prevention:** Razorpay, with the **webhook as the sole source of truth**. The server creates the provider order for the server-computed amount; the browser's success callback only unblocks the UI. `POST /webhooks/razorpay` verifies `X-Razorpay-Signature` over the **raw request body** and records the event in `payment_events` keyed on the provider event id, so replays are no-ops. An order reaches `Confirmed` only when a verified `payment.captured` event arrives. A daily reconciliation job diffs the provider's payment list against ours. COD is a separate, explicit payment method — not an unverified card.
→ plan §6.2

---

## Authorization

### L-05
### Any logged-in user can delete any review · 🔴 Critical

**Where:** [`backend/routes/productRoute.js:37-39`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/routes/productRoute.js#L37-L39)

```js
router.route("/reviews")
  .get(getProductReviews)
  .delete(isAuthenticatedUser, deleteReview);   // ← no authorizeRoles, no ownership check
```

`deleteReview` never compares the review's author to `req.user`. `DELETE /api/v1/reviews?id=<reviewId>&productId=<productId>` from any account deletes anyone's review.

Every other admin surface in the app *is* correctly gated with `authorizeRoles("admin")`. This one route was missed — which is the point: authorization enforced by remembering to add a middleware is authorization that will eventually be forgotten.

**Structural prevention:** resource-based authorization handlers (`ReviewAuthorOrStoreStaff`, `OrderOwnerOrStoreStaff`) instead of route-level middleware, so the check is a property of the resource rather than of the developer's memory. Backed at the database level by `unique (product_id, user_id)` and an `order_id` FK requiring a verified purchase. An integration test iterates **every** protected endpoint asserting 403 for a plain customer, so a newly added unguarded route fails CI.
→ plan §6.5

---

### L-06
### Every admin route guard on the frontend is inert · 🟠 High

**Where:** [`frontend/src/App.js:165-237`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/frontend/src/App.js#L165-L237) (nine occurrences) and [`ProtectedRoute.js:5,14`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/frontend/src/component/Route/ProtectedRoute.js#L5-L14)

`ProtectedRoute` reads `isAdmin` from **its own props**:

```jsx
const ProtectedRoute = ({ isAdmin }) => { ...
  if (isAdmin && user.role !== "admin") { return <Navigate to="/login" />; }
```

but `App.js` puts the prop on the **inner** `<Route>`:

```jsx
<Route element={<ProtectedRoute />}>                {/* ← nothing passed here */}
  <Route isAdmin={true} path="/admin/dashboard" element={<Dashboard />} />
</Route>
```

React Router ignores unknown props on `<Route>`, so `isAdmin` is always `undefined` and the branch never runs. All nine `/admin/*` pages render for any logged-in customer. The backend still returns 403, so this is information disclosure rather than data compromise — the admin UI renders as empty shells with error toasts — but the guard is doing *nothing at all*, silently, and nothing in the codebase would ever have told me.

**Structural prevention:** the frontend guard becomes cosmetic by design. Authorization lives on the endpoint (`RequireAuthorization("StoreStaff")` on the route group), and the 403-sweep integration test from L-05 is the permanent regression test. Client-side route guards exist only to avoid rendering a screen that would fail — never as the security boundary.
→ plan §6.5

---

### L-16
### Users cannot view their own orders; no ownership check exists · 🟡 Medium

**Where:** [`backend/routes/orderRoute.js:18`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/routes/orderRoute.js#L18)

`GET /order/:id` is gated `isAuthenticatedUser, authorizeRoles("admin")`, but the customer-facing order detail page calls it. Customers get 403 on their own orders.

`getSingleOrder` never compares `order.user` to `req.user._id`. It is only *accidentally* safe — the admin gate is doing work it was never meant to do. Loosen the gate to fix the customer bug and you immediately have an IDOR letting anyone read anyone's order and delivery address.

**Structural prevention:** an `OrderOwnerOrStoreStaff` policy expresses the actual rule — the owner *or* staff of the owning store, nobody else. Cross-tenant reads return **404, not 403**, so the API never confirms that another store's order id exists.
→ plan §6.5, §6.9

---

## Concurrency & integrity

### L-03
### Stock is never checked or reserved when an order is placed · 🔴 Critical

**Where:** [`backend/controllers/orderControllers.js:8-35`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/controllers/orderControllers.js#L8-L35)

`newOrder` performs no stock lookup. Stock is only touched much later, when an admin flips the order to `Shipped` (L-04) — and no admin UI to do that exists (L-20). In practice **stock was never decremented at all** in the running app.

The only quantity limit is client-side, in the cart component, against `product.Stock` that came from a `GET`. Unlimited overselling of a one-unit item requires no tooling.

**Structural prevention:** stock is reserved at `POST /checkout/start`, **before** payment, inside a transaction that also books the fulfilment slot — you can never hold stock without a slot or a slot without stock. Reservations carry a 15-minute TTL and are released by a background job. Placing an order without a live reservation is not an expressible state.
→ plan §5.2

---

### L-04
### Stock decrement is unawaited, non-atomic, and can kill the process · 🔴 Critical

**Where:** [`backend/controllers/orderControllers.js:97-121`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/controllers/orderControllers.js#L97-L121)

```js
if (req.body.status === "Shipped") {
    order.orderItems.forEach(async (o) => {      // ← forEach does not await
        await updateStock(o.product, o.quantity);
    });
}
...
async function updateStock(id, quantity) {
    const product = await Product.findById(id);
    product.Stock -= quantity;                   // ← read-modify-write, no guard
    await product.save({ validateBeforeSave: false });
}
```

Four distinct defects stacked in fifteen lines:

1. **`forEach` with an `async` callback does not await.** The 200 response is sent before any stock write settles.
2. **`updateStock` doesn't null-check `product`.** A deleted product throws inside an orphaned promise.
3. **That rejection reaches [`server.js:30-37`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/server.js#L30-L37)**, whose `unhandledRejection` handler calls `server.close()` and `process.exit(1)`. **One bad line item takes the entire server down.**
4. **Read-modify-write is a lost update.** Two concurrent shipments double-decrement, and `Stock` can go negative — nothing forbids it.

**Structural prevention:** a single atomic conditional statement, where "no rows affected" *is* the out-of-stock answer, with no read-then-write window:

```sql
UPDATE inventory SET reserved = reserved + @qty
 WHERE variant_id = @id AND on_hand - reserved >= @qty
```

Items are always applied in `variant_id` order (enforced in one place, `InventoryRepository.TryReserveAsync`, with a test) so concurrent multi-item orders cannot deadlock. Every stock movement writes an append-only `stock_ledger` row, so drift is detectable rather than invisible. Proven by an integration test firing **20 concurrent orders at 10 units of stock**: exactly 10 succeed, 10 return `OutOfStock`, `on_hand` lands on 0, and the ledger sums to zero.

There is no `process.exit` equivalent — a failed handler returns a `Result`, and the transaction rolls back.
→ plan §5.2

---

### L-15
### Cart is never cleared; no idempotency, so orders duplicate · 🟠 High

**Where:** [`frontend/src/constants/cartConstants.js`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/frontend/src/constants/cartConstants.js) and [`Payment.js:85-86`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/frontend/src/component/Cart/Payment.js#L85-L86)

`cartConstants.js` defines exactly three actions — `ADD_TO_CART`, `REMOVE_CART_ITEM`, `SAVE_SHIPPING_INFO`. **There is no `CLEAR_CART`.** Nothing empties the cart after checkout, and nothing removes `orderInfo` from `sessionStorage`. The customer lands on `/success` with a full cart; going back and resubmitting creates a second identical order at the stale total.

Compounding it, the order is fire-and-forget:

```js
dispatch(createOrder(order));   // not awaited
navigate("/success");           // runs regardless
```

A 500 from the API still shows "Order placed successfully".

**Structural prevention:** an `IdempotencyBehavior` in the command pipeline. The client generates an `Idempotency-Key` **once when the checkout screen mounts** — not per click — so a double-click, a retry and a flaky-network resend all collapse into one order. The server `INSERT`s the key first; a unique-constraint violation *is* the duplicate detection, with no lock. A completed key replays the stored response verbatim; the same key with a different request hash returns 422. `Cart.Clear()` happens inside the same transaction that creates the order, so the two can never disagree. The UI awaits the result before navigating.
→ plan §5.3

---

## Auth & session

### L-07
### A deleted user's token still authenticates, then 500s · 🟠 High

**Where:** [`backend/middleware/auth.js:14-18`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/middleware/auth.js#L14-L18)

```js
const decodedData = jwt.verify(token, process.env.JWT_SECRET);
req.user = await User.findById(decodedData.id);
next();                                    // ← no null check
```

If the user was deleted, `req.user` is `null`. `authorizeRoles` then reads `req.user.role` at [line 23](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/middleware/auth.js#L23) and throws → 500. On non-admin routes, handlers dereference `req.user._id` and 500 too.

More fundamentally: there is **no revocation of any kind**. No refresh tokens, no token version, no blocklist. Logout overwrites the cookie client-side; the JWT itself stays valid until expiry. Deleting a user or demoting an admin has no effect on a token already issued.

**Structural prevention:** ASP.NET Core Identity's `SecurityStamp` is validated on each request, so deleting a user or changing their role invalidates existing tokens by construction. Short-lived (15 min) access tokens plus opaque refresh tokens stored **hashed**, with **rotation and reuse detection**: presenting an already-rotated token means it leaked, so the whole token family is revoked and a security event is logged. A missing user is a 401, never a 500.
→ plan §6.5

---

### L-08
### Password is re-hashed on every save; breaks password reset · 🟠 High

**Where:** [`backend/models/userModel.js:49-54`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/models/userModel.js#L49-L54)

```js
userSchema.pre("save", async function (next) {
  if (!this.isModified("password")) {
    next();                                  // ← missing `return`
  }
  this.password = await bcrypt.hash(this.password, 10);
});
```

One missing keyword. Execution falls through, so the hash runs on **every** save.

In `forgotPassword` the document was loaded without `+password` (the field is `select: false`), so `this.password` is `undefined` and `bcrypt.hash(undefined, 10)` rejects — which, per L-04, reaches the `unhandledRejection` handler and **exits the process**. On any path that *does* load the password, it double-hashes and permanently locks the account out.

A one-character bug that bricks accounts and crashes the server. It is the strongest argument in this document for having tests.

**Structural prevention:** the application never hashes passwords. ASP.NET Core Identity owns credential storage, hashing, and its own upgrade path.
→ plan §6.5

---

### L-09
### Reset link is built from the `Host` header and points at the API · 🟠 High

**Where:** [`backend/controllers/userController.js:91-93`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/controllers/userController.js#L91-L93)

```js
const resetPasswordUrl = `${req.protocol}://${req.get("host")}/api/v1/password/reset/${resetToken}`;
```

Two bugs:

1. **It points at the API, not the app.** The API route is a `PUT`; the SPA route is `/password/reset/:token`. Clicking the emailed link issues a `GET` against a `PUT`-only path, which falls through to the SPA catch-all and returns `index.html`. **Password reset never worked.**
2. **`req.get("host")` is attacker-controlled.** A forged `Host` header sends the reset token to an attacker's domain — classic host-header poisoning, and the token is a full account takeover.

**Structural prevention:** all outbound URLs are built from a validated `Frontend:BaseUrl` configuration value, checked at startup. The `Host` header is never used to construct a URL. An E2E test walks the real reset flow, so "it never worked" is not a discoverable-in-production state.
→ plan §6.5

---

### L-10
### Auth cookie lacks `Secure`/`SameSite`; logout is a `GET` · 🟠 High

**Where:** [`backend/utils/jwtToken.js:6-18`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/utils/jwtToken.js#L6-L18), [`routes/userRout.js:29`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/routes/userRout.js#L29)

```js
const options = { expires: ..., httpOnly: true };     // no secure, no sameSite
res.status(statusCode).cookie("token", token, options).json({ success: true, user, token });
```

- `httpOnly` is set — the one thing done right — but **no `Secure`** (the cookie rides plaintext HTTP) and **no `SameSite`** (it is attached to cross-site requests).
- The JWT is **also returned in the JSON body**, where any XSS can read it, defeating much of the point of `httpOnly`.
- **Logout is a `GET`** — `router.route("/logout").get(logout)` — so `<img src="https://…/api/v1/logout">` on any page logs the user out. Harmless alone, but it is the same missing idea that makes state-changing `GET`s dangerous in general.

**Structural prevention:** access tokens live **in memory in Redux only** — never `localStorage`, never a cookie the API reads. The refresh token is opaque, stored hashed server-side, and delivered as `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`. Every state-changing operation is a `POST`/`PUT`/`DELETE`.
→ plan §6.5

---

### L-11
### No rate limiting or lockout; forgot-password enumerates users · 🟠 High

**Where:** [`backend/routes/userRout.js:23`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/routes/userRout.js#L23), [`userController.js:82-84`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/controllers/userController.js#L82-L84)

`POST /login` has no rate limit, no account lockout, no CAPTCHA, no backoff — passwords can be brute-forced at whatever rate the network allows. There is no `express-rate-limit` anywhere in the project. `/password/forgot` returns a distinct 404 "User not found", cleanly enumerating which email addresses hold accounts.

**Structural prevention:** ASP.NET Core's built-in rate limiter — fixed window (5/min/IP) on login, OTP and forgot-password; token bucket on search — plus Identity's built-in lockout after repeated failures. Forgot-password returns the **same response whether or not the account exists**.
→ plan §6.5

---

### L-12
### Unvalidated env vars and a swallowed database failure · 🟠 High

**Where:** [`backend/utils/jwtToken.js:8-10`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/utils/jwtToken.js#L8-L10), [`config/database.js:3-10`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/config/database.js#L3-L10)

Nothing validates configuration at boot. If `COOKIE_EXPIRE` is unset, `Date.now() + undefined * 86400000` is `NaN`, and `new Date(NaN)` makes Express throw `option expires is invalid` — **every login and registration 500s**. Same shape of failure for a missing `JWT_SECRET` or `JWT_EXPIRE`. All four are `sync: false` in `render.yaml`, i.e. hand-entered in a dashboard, so a single typo produces this.

Worse, a database connection failure is swallowed:

```js
mongoose.connect(process.env.DB_URI)
  .then(() => console.log("Connected to the database!"))
  .catch((err) => { console.error("Failed to connect to the database:", err); });
```

No rethrow, no exit. The server starts listening anyway, and the health check at `GET /api/v1` returns `"Hello World"` **without touching the database** — so the platform marks a completely broken deploy as healthy while every real request hangs until Mongoose's buffer timeout.

**Structural prevention:** options bound and validated at startup with `ValidateOnStart()`; a missing or malformed setting **fails the process immediately** rather than at the first login. `/health/live` and `/health/ready` are separate, and `ready` actually probes Postgres, Redis and object storage, and fails if EF Core reports pending migrations — so a broken deploy fails its probe instead of half-working.
→ plan §6.6, §8

---

## Injection

### L-13
### Regex injection / ReDoS in product search · 🟠 High

**Where:** [`backend/utils/appFeature.js:8-19`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/utils/appFeature.js#L8-L19)

```js
const keyword = this.queryStr.keyword ? { name: {
    $regex: this.queryStr.keyword,        // ← raw user input as a regex
    $options: "i",
}} : {};
```

The search box compiles user input as a regular expression. `?keyword=(a+)+$` is catastrophic backtracking against an **unindexed** `name` field — one unauthenticated GET pins the CPU. `?keyword=.*` dumps the catalogue.

**Structural prevention:** PostgreSQL full-text search through `EF.Functions.WebSearchToTsQuery`, which is a *parser*, not an evaluator — hostile input becomes search terms, never syntax. A GIN index on a generated `tsvector` column, with a `pg_trgm` similarity fallback for typos. An integration test fires injection payloads (`'; DROP`, `.*`, `{$gt:""}`) and asserts empty results rather than errors.
→ plan §6.4

---

### L-14
### NoSQL operator injection in the filter chain · 🟠 High

**Where:** [`backend/utils/appFeature.js:24-40`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/utils/appFeature.js#L24-L40)

```js
const removeFields = ["keyword", "page", "limit"];
removeFields.forEach((key) => delete queryCopy[key]);
let queryStr = JSON.stringify(queryCopy);
queryStr = queryStr.replace(/\b(gt|gte|lt|lte)\b/g, (key) => `$${key}`);
this.query = this.query.find(JSON.parse(queryStr));
```

Every query-string parameter except three is passed into `.find()`. The rewrite is a blind substring replace, so the token `gt` **anywhere** — including inside a legitimate string value — becomes `$gt`. Arbitrary Mongo operators reach the query: `?ratings[ne]=0`, `?user=<someone-else-id>`, and so on. There is no `express-mongo-sanitize` in the project.

**Structural prevention:** filters are a typed, explicitly-mapped query object; anything not on the allow-list is ignored rather than forwarded. EF Core parameterises every value, so a filter value can never become query syntax. Page size is capped server-side at 50 regardless of what is requested.
→ plan §3, §6.4

---

## Contract & correctness

### L-17
### Error contract mismatch — every error toast reads `undefined` · 🟡 Medium

**Where:** [`backend/middleware/error.js:33-36`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/middleware/error.js#L33-L36) vs. all four frontend action files (~30 sites)

The server responds `{ success: false, error: err.message }`. Every client reads `error.response.data.message` — a key that does not exist. **Every error message in the entire application displayed `undefined`.**

The same line has a second bug: on a network failure, timeout or CORS error, `error.response` is itself `undefined`, so the `catch` block throws a `TypeError`. The `FAIL` action never dispatches and the screen is stuck on its spinner forever.

Two mismatched string literals, never caught, because nothing typed the boundary between client and server.

**Structural prevention:** one response shape, always — **RFC 9457 ProblemDetails**, with a stable `type` URI and machine-readable `errors`. A single normalizer on the client turns it into a toast. Critically, the **TypeScript client is generated from the OpenAPI document in CI**, and the build fails if the committed client is stale — so a server-side contract change that the frontend hasn't adopted cannot merge. This class of bug stops being expressible.
→ plan §3, §10

---

### L-18
### Pagination is dead; the whole catalogue ships on every request · 🟡 Medium

**Where:** [`backend/controllers/productControllers.js:52,74`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/controllers/productControllers.js#L52-L74), [`frontend/src/reducers/productReducer.js:49`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/frontend/src/reducers/productReducer.js#L49)

Three defects producing one broken feature:

1. `const resultPerPage = 999;` — every request returns the entire catalogue, **including every product's base64 image blob**, which inflates the payload ~33% over the raw bytes and is uncacheable by any CDN.
2. The server sends `productCount`; the reducer reads `action.payload.productsCount`. Singular versus plural → `undefined` → `totalItemsCount={undefined}` on the pagination widget. (Same root cause as L-17.)
3. **No `.sort()` exists anywhere in the backend.** Result order is unspecified, so `skip`/`limit` paging would be non-deterministic even if it worked — items can repeat or vanish between pages.

**Structural prevention:** keyset (cursor) pagination over a deterministic sort key, `MaxPageSize` enforced server-side, and a single typed `PagedResult<T>` shared with the client through OpenAPI codegen. List queries project directly in the EF query, so only the needed columns leave the database — images are `object_key` strings served from a CDN, never bytes on the row.
→ plan §3, §4

---

### L-19
### Editing a review corrupts the rating average to 27 stars · 🟡 Medium

**Where:** [`backend/controllers/productControllers.js:181-207`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/backend/controllers/productControllers.js#L181-L207)

A **new** review coerces the rating: `rating: Number(rating)`. The **update** path does not — it assigns the raw string from the request body. The aggregate then does string concatenation:

```js
let avg = 0;
product.reviews.forEach((rev) => { avg += rev.rating; });   // 0 + "5" + 4 === "054"
product.ratings = avg / product.reviews.length;             // "054" / 2 === 27
```

A product displays **27 stars out of 5**. Alongside it: no null check on `product` (a bad id 500s), no clamp of `rating` to 1–5, and no check that the reviewer ever bought the item.

**Structural prevention:** `rating smallint CHECK (rating BETWEEN 1 AND 5)` at the database level, a `Rating` value object that cannot be constructed out of range, and the aggregate recomputed with a SQL `AVG` — never arithmetic over a loosely-typed array. `unique (product_id, user_id)` makes "edit" a real update rather than a second row, and an `order_id` FK enforces verified purchase.
→ plan §4

---

### L-20
### Placeholder components shipped to production · 🟡 Medium

**Where:** [`frontend/src/component/Order/OrderDetails.js`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/frontend/src/component/Order/OrderDetails.js), [`component/Admin/ProcessOrder.js`](https://github.com/YuvrajGitHub21/ECOMMERCE-MERN/blob/v1-legacy-node/legacy-node/frontend/src/component/Admin/ProcessOrder.js)

The customer order-detail screen is, in full:

```jsx
const OrderDetails = () => {
  return (
    <div>OrderDetails
      Nahi ho raha error solve
    </div>
  )
}
```

It is routed at `/order/:id` and linked from every row of "My Orders", so a customer clicking their own order sees the words *"the error isn't getting solved"* in production.

The admin counterpart is worse in a subtler way: `Admin/ProcessOrder.js` is a **verbatim copy of `Cart/ConfirmOrder.js`**. It ignores `useParams()`, reads `state.cart` instead of `state.orderDetails`, never dispatches `getOrderDetails` or `updateOrder`, and its button navigates the admin to `/process/payment`. The admin "process order" screen shows *the admin's own shopping cart*.

The consequence connects the whole document: **no admin could ever move an order to `Shipped`**, which is the only code path that decrements stock (L-04). The `updateOrder` action, its constants and its reducers all exist and are wired — nothing ever dispatches them. The feature looked complete from the inside.

**Structural prevention:** three Playwright E2E specs cover the flows that must work, including a staff member advancing an order through to pickup-code verification while asserting the customer's screen updates over SignalR. A screen that renders a placeholder fails CI. Order status transitions are a typed state machine with an explicit legal-transition matrix, unit-tested as a `[Theory]`, so "no path to Shipped" would be a failing test rather than a silent gap.
→ plan §5.5, §7

---

## Secondary findings

Real, but either lower impact or already covered by the structural changes above.

**Data model** — no index of any kind on any collection, so every filter and the `$regex` search is a collection scan. `pinCode` and `phoneNo` are typed `Number`, which silently destroys leading zeros and is wrong for a phone number in any case. `orderStatus` and `role` have no `enum`, so any string is a valid status or role. `createdAt` is hand-rolled; there is no `updatedAt` anywhere. `Product.images` uses `require:` instead of `required:` — a typo that disables the validation entirely. `maxLength` is applied to `Number` fields, where it does nothing. Reviews are an unbounded embedded array on a document that also carries base64 images, against a hard 16 MB BSON ceiling.

**Mass assignment** — `Product.create(req.body)` and `findByIdAndUpdate(req.params.id, req.body)` let an admin set `reviews`, `ratings`, `numOfReviews`, `user` and `_id` directly. Admin-only, so low severity, but the shape is wrong.

**API 404s return HTML** — `express.static` plus the `GET *` SPA fallback are registered *before* the error middleware, so any unmatched GET — including `/api/v1/does-not-exist` — returns `index.html` with **200**. This actively hid a live bug: `userAction.js` calls `/api/v1/admin/user/${id}` while the route is `/admin/users/:id`, so the admin edit-user form silently received HTML and never populated.

**Refresh flash-redirect** — `userReducer`'s initial state leaves `loading` as `undefined`, so `ProtectedRoute` falls through to `!isAuthenticated` on first render and bounces to `/login` before `loadUser()` resolves. Every hard refresh on a protected page logs you out visually.

**Unused attack surface** — `express-fileupload` is mounted with no `limits` and no `abortOnLimit`, and nothing in the codebase reads `req.files`. Combined with a 13 MB JSON body limit on unauthenticated `POST /register`, it is a memory-exhaustion vector on a 512 MB instance, in exchange for zero functionality.

**Dependency hygiene** — `@material-ui/core` v4 (EOL, not React 18 compatible) and `@mui/material` v5 are **both** installed and both used, which is why the project needs `--legacy-peer-deps` to install at all; two full component libraries ship in the bundle. `body-parser` and `mongodb` are dependencies that nothing imports. `nodemon` is in `dependencies`, so it installs on every production deploy. The CRA dev proxy is hardcoded to `http://192.168.179.1:4000` — a private LAN address that works on exactly one machine. Source maps are generated and served in production, exposing the unminified source.

**Domain drift** — the admin product category list is `["Laptop","Footwear","Bottom","Tops","Attire","Camera","SmartPhones"]`, hardcoded and duplicated in two files, in what is supposed to be a grocery store.

---

## What I'd do differently

The through-line is that I treated the browser as part of my system. Prices, totals, paid status, quantities and roles all arrived from the client and were written down as facts. Almost every Critical finding here is one instance of that single idea.

The second theme is that nothing was ever verified end to end. Password reset never worked. Pagination never worked. No admin could ship an order, so stock was never decremented at all. Each of these was invisible from the inside because each piece looked plausible in isolation — the action existed, the reducer existed, the route existed. There were no tests, so "wired up" and "working" were indistinguishable.

What I'd keep: the product idea, and the decision to model orders and fulfilment explicitly rather than bolting them onto products.

What changes in the rewrite isn't mainly the language. It's that the server now computes anything that matters, invariants live in the database and the type system rather than in my memory, and the flows that must work are covered by tests that fail loudly when they don't.
