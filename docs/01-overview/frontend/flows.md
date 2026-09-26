# Client flows

End-to-end sequences a client implements, assembled from the per-service contracts. Each diagram names the calls in
order; the prose below it says what to poll, how long to expect, and what quirk to design around. Nothing here was
re-probed for this file — every call, status and timing figure is already verified in the linked service file, and
this file is a synthesis of them.

**Verified at:** `85b69f5` on `feature/admin-panel` (2026-09-25) — the same commit as every service file it draws on.

---

## Contents

- [1. Sign-up and sign-in](#1-sign-up-and-sign-in)
- [2. Browse to a paid order](#2-browse-to-a-paid-order)
- [3. Cancel and refund](#3-cancel-and-refund)
- [4. Admin: product edit](#4-admin-product-edit)
- [5. Admin: bulk actions, import and export](#5-admin-bulk-actions-import-and-export)
- [6. Admin: user management](#6-admin-user-management)

---

## 1. Sign-up and sign-in

```mermaid
sequenceDiagram
    actor U as Shopper
    participant GW as Gateway
    participant ID as Identity

    U->>GW: POST /api/v1/auth/register
    GW->>ID: (proxied, anonymous)
    ID-->>U: 200 { userId, email, message }
    Note over U,ID: message promises a confirmation email.<br/>None is sent (F-27). Do not build a confirm step.

    U->>GW: POST /api/v1/auth/login { email, password }
    GW->>ID: (proxied)
    alt 2FA is off
        ID-->>U: 200 { accessToken, refreshToken, expiresIn, requires2FA:false, user }
    else 2FA is on
        ID-->>U: 200 { accessToken:"", refreshToken:"", expiresIn:0, requires2FA:true, user:null }
        U->>GW: POST /api/v1/auth/login (same body + twoFactorCode)
        GW->>ID: (proxied)
        ID-->>U: 200 { accessToken, refreshToken, expiresIn, requires2FA:false, user }
    end

    Note over U: Use accessToken for 60 min. Store user.roles —<br/>do not decode the JWT for authorization.

    loop Every ~55 min, or on a 401
        U->>GW: POST /api/v1/auth/refresh-token { refreshToken }
        GW->>ID: (proxied)
        ID-->>U: 200 { accessToken, refreshToken, expiresIn }
        Note over U: The old refreshToken is now dead.<br/>Store the new one before anything else.
    end

    U->>GW: POST /api/v1/auth/revoke-token { refreshToken }
    GW->>ID: (proxied)
    ID-->>U: 204
    Note over U: Access token still works until it expires (up to 60 min).<br/>Drop it client-side too.
```

- **Registration signs nothing up for email confirmation in practice.** [`POST /auth/register`](identity.md#post-apiv1authregister)
  answers with a message that promises a confirmation email, but none is sent (F-27, deferred by the owner). The
  account can log in immediately. Do not add a "check your email" step, and do not build a UI for
  [`POST /auth/confirm-email`](identity.md#post-apiv1authconfirm-email) — no user can call it successfully, since
  registration gives them nothing to send.
- **Branch on `requires2FA`, never on the HTTP status.** Both the plain and the 2FA-pending answers are 200
  ([`POST /auth/login`](identity.md#post-apiv1authlogin)). Re-send the exact same login request with `twoFactorCode`
  added once the user enters a code from their authenticator app.
- **Recovery codes cannot be redeemed** (F-29): if a 2FA user loses their device, the only way back in is an admin
  running [`disable-2fa`](identity.md#post-apiv1adminusersiddisable-2fa) on their account. Do not tell users the
  codes shown at enrollment will let them sign in.
- **Refresh rotates the token, one at a time.** [`POST /refresh-token`](identity.md#post-apiv1authrefresh-token)
  invalidates the old refresh token as soon as it is used; run one refresh at a time (a parallel second refresh with
  the same token gets `Auth.InvalidToken` or, rarely, `Auth.TokenAlreadyUsed`) and store the new pair before any other
  request that might itself trigger a refresh.
- **A `password` field can also lock the account for 15 minutes** ([Failed logins and
  lockout](identity.md#failed-logins-and-lockout)): three wrong attempts throttle the account with an
  exponentially-growing delay (the reported wait itself is meaningful since the `bb8c148` fix), and a fifth locks it
  for 10 minutes. Any 401 `Auth.TooManyAttempts` should show the server's `detail`, not a generic "wrong password".
- **Forgot/reset password** is a separate, unauthenticated flow: `POST /auth/forgot-password` always answers the same
  200 regardless of whether the address exists (no user enumeration); the email's link
  (`<ResetUrlBase>?userId=…&token=…`, both URL-encoded) must be served by the frontend at the configured base path,
  and posts to [`POST /auth/reset-password`](identity.md#post-apiv1authreset-password), which revokes every session.
  The same reset page also completes an admin-created account that was invited with no password (see
  [§6](#6-admin-user-management)).
- **Logout is a revoke, not a token blacklist.** [`POST /revoke-token`](identity.md#post-apiv1authrevoke-token) is
  204 and idempotent even for an unknown token; the client must still discard its own access token, since the server
  cannot invalidate one early.

---

## 2. Browse to a paid order

```mermaid
sequenceDiagram
    actor U as Shopper
    participant GW as Gateway
    participant CAT as Catalog
    participant BAS as Basket
    participant ORD as Ordering
    participant PAY as Payment
    participant STR as Stripe

    U->>GW: GET /api/v1/products, /categories (anon)
    GW->>CAT: (proxied)
    CAT-->>U: 200 product/category pages

    U->>GW: POST /api/v1/basket/{userId}/items
    GW->>BAS: (proxied)
    BAS-->>U: 204

    U->>GW: POST /api/v1/basket/{userId}/checkout { shippingAddress }
    GW->>BAS: (proxied)
    Note over BAS,CAT: Basket re-reads every line from Catalog first.<br/>A stale line answers 409 with a "lines" array — nothing is ordered.
    BAS-->>U: 200 { checkoutId }
    BAS--)ORD: BasketCheckedOut event (async)

    Note over U,ORD: ~1 s later (observed)
    loop poll with back-off
        U->>GW: GET /api/v1/users/{userId}/orders
        GW->>ORD: (proxied)
        ORD-->>U: 200 page — take the newest order
    end
    ORD--)PAY: OrderCreated event (async)

    Note over ORD,PAY: 1 s to ~15 s later (observed 1 s, 8 s, 14 s)
    loop poll create-intent with back-off until it is 200
        U->>GW: POST /api/v1/payments/create-intent { orderId }
        GW->>PAY: (proxied)
        alt payment record not created yet
            PAY-->>U: 409 PAYMENT_NOT_READY
        else record exists
            PAY->>STR: create (or resume) the payment intent
            PAY-->>U: 200 { clientSecret, status }
        end
    end

    U->>STR: Stripe.js confirmPayment(clientSecret) — in the browser
    alt card accepted
        STR--)PAY: webhook payment_intent.succeeded
        PAY-->>PAY: payment turns Success
        PAY--)ORD: PaymentSuccessEvent (async)
        Note over PAY,ORD: ~14 s later (observed)
        loop poll with back-off
            U->>GW: GET /api/v1/orders/{id}
            GW->>ORD: (proxied)
            ORD-->>U: 200 — status becomes Paid (1)
        end
    else card declined
        STR-->>U: declined, reported by Stripe.js in the browser
        Note over PAY: payment stays Processing, errorMessage set.<br/>Same clientSecret still works — offer "try another card".
    end
```

- **There is no endpoint that returns an order id from a `checkoutId`.** Poll
  [`GET /users/{userId}/orders`](ordering.md#get-apiv1usersuseridorders) (newest first) and take the top row; do not
  try to correlate by amount or item list, since a second concurrent checkout would race it.
- **A repeated checkout call is safe.** [`POST /{userId}/checkout`](basket.md#post-apiv1basketuseridcheckout) returns
  the same `checkoutId` for a retry after a lost response, as long as the basket has not been re-created since. A
  revalidation 409 means the
  basket was updated to the catalog's current prices/stock in the same response — show the `lines` array, let the
  shopper review, and re-submit the identical request.
- **`create-intent` is both "create" and "resume".** Call it once to get the `clientSecret`; call it again — after a
  reload, in a second tab, or because the first response was lost — and it returns the **same** intent and secret
  while the payment is still payable (F-52, fixed). Do not cache the secret only in memory: store it (for example in
  `sessionStorage`) so a reload does not orphan the payment, but treat a fresh `create-intent` call as the source of
  truth.
- **The publishable key is not served by the API.** Stripe.js needs the publishable key from the frontend's own
  configuration, from the same Stripe account as the server's secret key ([payment.md](payment.md#how-a-customer-pays-the-card-flow)).
- **Poll with back-off, not a fixed delay**, for every asynchronous step above: 1 s, 2 s, 4 s, … up to about 30 s
  (the outbox processor's idle back-off). The observed figures (1 s / 8 s–15 s / 14 s) are typical, not guaranteed.
  See [conventions.md §9](conventions.md#9-consistency-and-caching) for the full table.
- **Changing the order's items while it is Pending is safe and reaches Payment.** Adding, updating or removing a
  line updates `Order.totalPrice`, and Payment revises its own `amount` — and an open Stripe intent's amount — within
  about 2 s ([ordering.md](ordering.md#post-apiv1ordersiditems), [payment.md](payment.md#payment-status-and-method)).
  The same `clientSecret` keeps working and now charges the new total; no new `create-intent` call is needed.

---

## 3. Cancel and refund

```mermaid
sequenceDiagram
    actor U as Shopper/Admin
    participant GW as Gateway
    participant ORD as Ordering
    participant PAY as Payment

    rect rgb(235, 245, 255)
    Note over U,PAY: Cancel — only while the order is Pending
    U->>GW: POST /api/v1/orders/{id}/cancel { reason }
    GW->>ORD: (proxied)
    alt order is Pending
        ORD-->>U: 204 — status becomes Cancelled (4)
        ORD--)PAY: order-cancelled message (async)
        PAY-->>PAY: payment turns Cancelled (no money was ever taken)
    else order is Paid or later
        ORD-->>U: 409 Order.NotCancellable
        Note over U: "Only pending orders can be cancelled,<br/>this order is paid." — offer a refund instead
    end
    end

    rect rgb(255, 245, 235)
    Note over U,PAY: Refund — admin only, on a captured (Success) payment
    actor A as Admin
    A->>GW: POST /api/v1/payments/{id}/refund { reason? }
    GW->>PAY: (proxied, Admin role)
    alt payment is Success
        PAY-->>A: 200 Payment { status: "Refunded" }
        PAY--)ORD: refund message (async)
        Note over PAY,ORD: ~9 s later (observed)
        ORD-->>ORD: order becomes Refunded (5)
    else payment already refunded / not captured
        PAY-->>A: 409 PAYMENT_ALREADY_REFUNDED / PAYMENT_NOT_CAPTURED
    end
    end
```

- **There is no customer "cancel after payment."** Once an order is Paid, [`POST /{id}/cancel`](ordering.md#post-apiv1ordersidcancel)
  answers 409 `Order.NotCancellable`; the only way back is an admin refund
  ([ordering.md](ordering.md#post-apiv1ordersidcancel)).
- **Refunds are always full**, never partial: `amount`, if sent at all, must equal the payment's own amount exactly
  ([`POST /{id}/refund`](payment.md#post-apiv1paymentsidrefund)).
- **A refund moves real money only for a card payment.** Stripe refunds in about 1 s. An offline or
  simulator-settled payment is only marked `Refunded` in the database — the money must be returned outside the
  system.
- **The order's status change is asynchronous either way**, by message, not by the cancel/refund call itself: expect
  the order to still read its old status for a few seconds after a 204/200. Poll
  [`GET /orders/{id}`](ordering.md#get-apiv1ordersid) with back-off.
- **A cancelled order is never refunded automatically.** If a payment already succeeded before the cancel request
  raced it, Payment does not undo it — that state is reconciled by an admin refund, not by the cancel flow.

---

## 4. Admin: product edit

```mermaid
sequenceDiagram
    actor A as Admin
    participant GW as Gateway
    participant CAT as Catalog
    participant CACHE as Redis

    A->>GW: PUT /api/v1/products/{id} { …fields }
    GW->>CAT: (proxied, Admin role)
    CAT->>CACHE: evict products:list family, this product's detail key
    CAT-->>A: 204

    A->>GW: PATCH /api/v1/products/{id}/stock { delta | absolute }
    GW->>CAT: (proxied, Admin role)
    CAT-->>A: 200 ProductStockResponse

    Note over A,CAT: The storefront's own next read is never stale —<br/>the write evicts the cache before answering.
    actor U as Shopper
    U->>GW: GET /api/v1/products/{id}
    GW->>CAT: (proxied, anon)
    CAT-->>U: 200 — reflects the edit at once
```

- **A new product starts as a Draft**, invisible to the storefront, until
  [`POST /{id}/publish`](catalog.md#post-apiv1productsidpublish) — see the [product lifecycle
  diagram](catalog.md#product-lifecycle). Publish/unpublish are idempotent.
- **`PUT` and `POST` normalise differently** (F-41): create stores the name untrimmed, update trims it. Trim on the
  client before either call to avoid the difference being visible at all.
- **Stock has no built-in floor beyond zero-or-negative refusal**, and the endpoint is safe to call concurrently:
  concurrent `+1` deltas from multiple admin sessions are each applied in turn (observed 8 parallel `+1` calls all
  succeed and sum correctly) — [`PATCH /{id}/stock`](catalog.md#patch-apiv1productsidstock).
  **Ordering itself checks no stock** when an order is created directly with `POST /orders` rather than through
  Basket checkout ([ordering.md](ordering.md#post-apiv1orders)) — worth knowing before building an admin
  order-creation screen on that endpoint.
- **Deleting sets the product Discontinued**, freeing its SKU; restoring always comes back as a Draft, so the admin
  must publish it again to make it visible ([Product lifecycle](catalog.md#product-lifecycle)).
- **Moving or deleting the product's category has consequences the product edit screen should guard against**:
  restoring a product whose category was deleted makes it unreachable by id (F-43), and `PUT
  /categories/{id}/parent` with an empty body silently re-roots a category (F-38). Neither is fixed; both are
  documented with a ⚠ in [catalog.md](catalog.md).
- **Cache invalidation is automatic on every write** — an admin never needs to call
  [`POST /admin/cache/invalidate`](catalog.md#cache) after an ordinary edit. That endpoint exists for recovering
  from an inconsistency, not as a step in the normal edit flow.

---

## 5. Admin: bulk actions, import and export

```mermaid
sequenceDiagram
    actor A as Admin
    participant GW as Gateway
    participant CAT as Catalog

    A->>GW: POST /api/v1/products/bulk/{publish|unpublish|delete|category|price}
    GW->>CAT: (proxied, Admin role, rate limit "bulk")
    CAT-->>A: 200 BulkProductReport { requested, succeeded, failed, items[] }
    Note over A: Each id's outcome is independent —<br/>read items[] rather than trusting the overall 200

    A->>GW: POST /api/v1/products/import (CSV or rows)
    GW->>CAT: (proxied, 8 MiB body cap)
    CAT-->>A: 200 ProductImportReport { one result per row }
    Note over CAT: Create-only. A row for an existing SKU<br/>is a per-row failure, not an update

    A->>GW: GET /api/v1/products/export?…filters
    GW->>CAT: (proxied, same filters as the product list)
    CAT-->>A: 200 text/csv (BOM, CRLF, PascalCase headers, 10 000-row cap)
```

- **A bulk report is per-item, not all-or-nothing.** [`POST /bulk/{action}`](catalog.md#bulk-product-actions)
  answers 200 even when some ids failed; the admin UI must read `report.items[]`, not just the overall status. Only
  `bulk/category` can refuse the whole request up front (an unknown target category), before touching any row.
- **A repeated id in the request is refused outright, not de-duplicated** — the batch is rejected with 400, forcing
  the client to fix its own list rather than silently picking a winner.
- **Import never updates.** [`POST /products/import`](catalog.md#product-import) checks every row exactly as
  `POST /products` would, so a row naming an existing SKU is a per-row failure in `ProductImportReport`, not a
  silent update — there is no bulk-edit-by-CSV path.
- **Export shares the product list's filters** ([`GET /products/export`](catalog.md#product-export)), so a
  filtered admin grid and its "export this view" button should send the same query string. The CSV writes the enum
  by name (`Active`), exactly as the JSON list does ([conventions.md, enum table](conventions.md#enums)).
- **The gateway's general 1 MiB body cap does not apply to import** — it gets its own 8 MiB cap, big enough for
  about 1 000 rows; bulk actions and everything else stay under the general cap.
- **A cache family is bumped once per request, not once per row** — a 1 000-item bulk publish costs one
  `products:list` eviction, not a thousand.

---

## 6. Admin: user management

```mermaid
sequenceDiagram
    actor A as Admin
    participant GW as Gateway
    participant ID as Identity
    participant MAIL as Notification/Mailpit

    alt admin sets a password
        A->>GW: POST /api/v1/admin/users { email, …, password }
        GW->>ID: (proxied, Admin role)
        ID-->>A: 201 { userId, inviteSent:false }
        Note over A: The user can sign in immediately with that password
    else admin leaves password unset
        A->>GW: POST /api/v1/admin/users { email, … }
        GW->>ID: (proxied, Admin role)
        ID-->>A: 201 { userId, inviteSent:true }
        ID--)MAIL: "Reset your EShop password" email
        Note over MAIL: Same link and same landing page as<br/>a self-service forgot-password (see §1)
    end

    A->>GW: PUT /api/v1/admin/users/{id}/roles { roles:[...] }
    GW->>ID: (proxied, Admin role, roles.manage)
    ID-->>A: 204
    Note over ID: Takes effect on the user's NEXT login.<br/>Their current access token keeps its old roles for up to 60 min

    A->>GW: POST /api/v1/admin/users/{id}/lock { until, reason }
    GW->>ID: (proxied)
    ID-->>A: 204
    actor U as That user
    U->>GW: POST /api/v1/auth/login
    GW->>ID: (proxied)
    ID-->>U: 401 Auth.InvalidCredentials
    Note over ID: A locked or deactivated account looks<br/>exactly like a wrong password to the user

    A->>GW: POST /api/v1/admin/users/{id}/unlock
    GW->>ID: (proxied)
    ID-->>A: 204
    Note over ID: Also clears the failed-login throttle (F-28),<br/>which "lock" alone does not
```

- **An invited user completes their own account through the password-reset page**, not a separate "accept invite"
  endpoint: `inviteSent: true` sends the same email as
  [`forgot-password`](identity.md#post-apiv1authforgot-password), landing on the same
  `/reset-password` page ([`POST /admin/users`](identity.md#post-apiv1adminusers)). Build one reset-password page,
  not two.
- **A role change is not retroactive on an issued token.** `PUT /{id}/roles` changes what the *next* login's token
  contains; a session already open keeps its old `roles` claim for up to 60 minutes. Don't promise "permissions
  applied immediately" in the UI copy.
- **Deleting a role does not evict members' cached role claim either** (F-33): for up to 5 minutes after
  `DELETE /roles/{id}`, a member's freshly issued token can still carry the deleted role name, even though the same
  response's `user.roles` (from `/auth/login`) already omits it.
- **Locked, deactivated and deleted accounts are indistinguishable to the end user** — all three answer 401
  `Auth.InvalidCredentials` at login, deliberately, to avoid leaking account state. The admin screen is the only
  place that can tell them apart (`GET /admin/users/{id}`'s own status field).
- **Unlock does two things `lock`'s opposite doesn't**: it clears both the admin-set lock *and* the automatic
  failed-login throttle (F-28's fix). A support agent who only knows about "lock" may not realise a still-blocked
  login after unlocking is the throttle, not the lock, reasserting itself — advise re-running unlock, which is
  idempotent.
- **Deactivate/delete/revoke-tokens all revoke refresh tokens**, but not the access token already in the user's
  browser — it keeps working for up to 60 minutes ([identity.md, "When the account is no longer
  usable"](identity.md#when-the-account-is-no-longer-usable)). Where an admin needs an *immediate* cutoff (a
  compromised account), there is no faster mechanism than that window.

---

## Related documents

- [conventions.md §4](conventions.md#4-authentication) — the auth building blocks these flows compose
- [conventions.md §9](conventions.md#9-consistency-and-caching) — the full table of observed asynchronous delays
- [identity.md](identity.md), [basket.md](basket.md), [ordering.md](ordering.md), [payment.md](payment.md),
  [catalog.md](catalog.md) — the endpoint-level detail behind every step above

---

**Version**: 1.0  
**Last Updated**: 2026-09-25
