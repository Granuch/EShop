# Basket API

The shopper's basket — add, update, remove, clear, checkout — plus the admin views of stored baskets, abandoned
baskets and the checkout outbox's dead letters.

**Verified at:** `ec600da` (`feature/admin-panel`, 2026-09-25). Basket's code, `BuildingBlocks` and the gateway have
not changed since `105d647`. Every endpoint in this file was checked against the C# source and the service's
OpenAPI document, and called through the gateway on the compose `sandbox` stack. Shared rules (errors, paging, rate
limits, CORS) are in [conventions.md](conventions.md) and are not repeated here.

## Base paths through the gateway

| Path | Methods | Gateway policy | Service policy | Audience |
|---|---|---|---|---|
| `/api/v1/basket/{userId}`, `/{userId}/items/**` | GET, HEAD | `Authenticated` | owner **or** admin | Storefront |
| `/api/v1/basket/{userId}`, `/{userId}/items/**`, `/{userId}/checkout` | POST, PUT, DELETE | `Authenticated` | owner only | Storefront |
| `/api/v1/basket/admin/carts`, `/abandoned` | GET | `Admin` role | `baskets.read` | Admin panel |
| `/api/v1/basket/admin/outbox/dead-letters/details` | GET | `Admin` role | `system.manage` | Admin panel |
| `/api/v1/basket/admin/outbox/dead-letters` | GET | `Admin` role | `Admin` role | Admin panel |
| `/api/v1/basket/admin/outbox/dead-letters/replay` | POST | `Admin` role | `Admin` role | Admin panel |

Not routed through the gateway: Basket's own `GET /api/v1/admin/audit` does not exist — Basket has no database and
is not part of the merged audit trail ([admin-platform.md](admin-platform.md)).

**Basket differs from the other services in six ways.** Read these once before using any endpoint below:

- **There is no anonymous basket and no merge-on-login.** Every route requires a signed-in caller; there is no
  session-cookie basket to merge into an account at login. Build the storefront's "add to basket" around an
  authenticated user from the start.
- **The owner check is case-sensitive (ordinal).** The route's `{userId}` must match the token's subject exactly,
  byte for byte. A client that mixes the case of an id it already has (rather than using the `id` a login response
  gave verbatim) is treated as someone else's basket: 403, not 200.
- **An admin may only read another user's basket** (`GET`/`HEAD`), never write it — not even to clear it. Every
  write path checks the caller against the route `{userId}`, regardless of role.
- **Every command validates through the generic `Result<T>`, so every validation failure is shape (a),
  `Validation.Failed`** ([conventions.md §3.3](conventions.md#33-validation-errors-three-shapes)) — Basket has no
  shape (b) endpoints at all, unlike Catalog and Identity.
- **No caching.** There is no `Cache-Control` guidance to give: every read is live against Redis, and every write is
  visible on the very next read.
- **Basket's own outbox is a Redis list, not the EF outbox the other services use.** Checkout enqueues to it inside
  the same Redis transaction that clears the basket, so "checkout succeeded" and "the order will be created" are the
  same fact. A message that cannot be published after retrying for hours becomes a dead letter — see
  [Outbox dead letters](#outbox-dead-letters).

## Contents

- [Storefront / customer](#storefront--customer)
  - [Get basket](#get-apiv1basketuserid)
  - [Add an item](#post-apiv1basketuseriditems)
  - [Update an item's quantity](#put-apiv1basketuseriditemsproductid)
  - [Remove an item](#delete-apiv1basketuseriditemsproductid)
  - [Clear the basket](#delete-apiv1basketuserid)
  - [Checkout](#post-apiv1basketuseridcheckout)
- [Admin panel](#admin-panel)
  - [Stored baskets](#stored-baskets)
  - [Abandoned baskets](#abandoned-baskets)
  - [Outbox dead letters](#outbox-dead-letters)
- [Types](#types)
- [Frontend notes](#frontend-notes)

---

## Storefront / customer

Every endpoint below is under `/api/v1/basket/{userId}`. **Gateway:** `Authenticated` — any signed-in caller reaches
the service; the service then decides who may act on `{userId}`. **Service:** the `BasketOwnerOrAdminRead` policy —
the caller's token subject must equal `{userId}` exactly (ordinal comparison) for **any** method, or the caller must
hold the `Admin` role **and** the request must be `GET` or `HEAD`. There is no permission-based path here and no
"my basket" shortcut: the client must know its own user id (from the login response) and always send it.

**Rate limit:** none of Basket's own. Only the global limiter applies — 100 requests per 60 seconds per client IP,
shared across every route in the service ([conventions.md §7](conventions.md#7-rate-limits)).

### `GET /api/v1/basket/{userId}`

The caller's basket. Source: `GetBasketQuery`.

**200** [`Basket`](#basket). A user with no stored basket gets an **empty one**, never a 404: `items: []`,
`totalPrice: 0`, `totalItems: 0`, `createdAt: null`, `lastModifiedAt: null`. Captured live:

```json
{"userId":"0749b287-0897-408f-b248-5f133aab1796","items":[],"totalPrice":0,"totalItems":0,
 "createdAt":null,"lastModifiedAt":null}
```

With items (after two adds of the same product, merged into one line):

```json
{"userId":"0749b287-0897-408f-b248-5f133aab1796",
 "items":[{"productId":"9316561d-b960-46e2-862d-b7aceea4b77d","productName":"Domain-Driven Design",
   "price":42.00,"quantity":5,"subTotal":210.00}],
 "totalPrice":210.00,"totalItems":5,
 "createdAt":"2026-09-25T09:55:09.8689808Z","lastModifiedAt":"2026-09-25T09:55:10.3519532Z"}
```

`price` is the line's price **at the time it was added or last refreshed** — the effective price
(`discountPrice ?? price`) Catalog reported then, not necessarily today's. Adding a discounted product live returns
its `discountPrice`:

```json
{"userId":"0749b287-0897-408f-b248-5f133aab1796",
 "items":[{"productId":"114176e0-23c3-418f-847c-440744ff2fdc","productName":"Phantom X12",
   "price":799.99,"quantity":1,"subTotal":799.99}],
 "totalPrice":799.99,"totalItems":1,
 "createdAt":"2026-09-25T09:55:28.3341936Z","lastModifiedAt":"2026-09-25T09:55:28.3342023Z"}
```

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | Never observed in practice: `userId` comes from the route and cannot be empty |
| 503 | `Basket.OperationFailed` | Redis is unreachable (from source; not forced live) |

- **`totalItems` is a JSON number that can exceed 2^53 only in theory** — it is serialized from a .NET `long`, kept
  wide because a basket document stored before the per-line cap existed could hold a line near `int.MaxValue`.
  Ordinary baskets never approach this.
- Admins may call this for **any** `userId` (still `GET`/`HEAD` only): confirmed live (200), with **no query
  parameter or extra field** distinguishing "my basket" from "a basket I'm allowed to read as an admin".

### `POST /api/v1/basket/{userId}/items`

Add a product, or increase its quantity if it is already in the basket. Source: `AddItemToBasketCommand`. Catalog is
read once, by the service, over the internal network (not the caller's token) — a **product the anonymous public
catalog cannot see (draft, discontinued, deleted) cannot be added**, even by an admin.

**Body** [`AddItemRequest`](#additemrequest):

| Field | Type | Required | Notes |
|---|---|---|---|
| `productId` | string (GUID) | yes | Must be a currently **Active** product |
| `quantity` | integer | yes | 1–999. Server-computed if the product is already in the basket: the **merged** total (existing + this request) must still be ≤ 999 and ≤ Catalog's current stock |

**204** on success — no body. **Only `productId` and `quantity` are read.** A `productName` or `price` sent in the
body is silently ignored: every price and name in a basket comes from Catalog, never from the client (this used to
be client-settable and the OpenAPI document still lists them — send them and they are dropped).

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `quantity` ≤ 0 or > 999 (`"Quantity: Quantity must be greater than zero"` / `"...cannot exceed 999"`); malformed JSON gives a **bare 400 with an empty body**, not this shape ([conventions.md §2](conventions.md#unknown-and-malformed-request-bodies), F-20) |
| 400 | `Basket.ValidationFailed` | A domain rule the validator cannot see: the **merged** quantity would exceed 999 (validator only checks this request's own number), or the basket would exceed 100 distinct products. From source, not observed live |
| 404 | `Basket.ProductNotFound` | `productId` does not exist, or is not **Active** in the public catalog (draft, discontinued, deleted all read as "not found" here) |
| 409 | `Basket.InsufficientStock` | The merged quantity exceeds Catalog's current stock. Checked again, authoritatively, at [checkout](#post-apiv1basketuseridcheckout) |
| 503 | `Basket.ProductVerificationFailed` | Catalog is unreachable or times out |

Captured error, stock 10 and a request for 11:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Conflict","status":409,
 "detail":"Not enough of this product is in stock for that quantity.","errorCode":"Basket.InsufficientStock",
 "traceId":"00-685aa856e1965ba3e1257f452000de17-ee7d7c0db25f81f7-00"}
```

- **Adding the same product twice merges the quantities** into one line and refreshes its stored `productName` and
  `price` to Catalog's current values — so a missed price-change event is repaired the next time anyone adds that
  product, even by one unit.
- Unknown JSON properties are ignored, not refused, unlike Catalog ([conventions.md §2](conventions.md#unknown-and-malformed-request-bodies)).

### `PUT /api/v1/basket/{userId}/items/{productId}`

Set a line to an exact quantity. Source: `UpdateBasketItemQuantityCommand`.

**Body** [`UpdateQuantityRequest`](#updatequantityrequest): `{ "quantity": number }`, required, 0–999.

**204** on success — no body. **`quantity: 0` removes the line** (the same as
[DELETE](#delete-apiv1basketuseriditemsproductid)), rather than being refused.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `quantity` < 0 or > 999 (`"...must be greater than or equal to zero"` / `"...cannot exceed 999"`) |
| 404 | `Basket.NotFound` | The caller has no stored basket at all |
| 404 | `Basket.ItemNotFound` | The basket exists but does not hold `productId` |
| 409 | `Basket.ConcurrentUpdate` | Five internal retries all lost a write race (from source; the basket kept changing under concurrent requests) |

Unlike [add](#post-apiv1basketuseriditems), this does **not** re-check stock or re-read Catalog: it can set a
quantity above what Catalog now has in stock. Checkout is what enforces the current stock authoritatively.

### `DELETE /api/v1/basket/{userId}/items/{productId}`

Remove one line. Source: `RemoveBasketItemCommand`.

**204** on success — no body, **whether or not the basket held that product**, with one exception:

| Status | `errorCode` | When |
|---|---|---|
| 404 | `Basket.NotFound` | The caller has **no stored basket at all** — removing from a basket that does not exist is refused, not treated as already-done (⚠ below) |

Removing a product the basket does not hold — while the basket itself exists, with other lines in it — is a plain
204: confirmed live, adding one product then deleting a different, never-added one from the same basket.

### `DELETE /api/v1/basket/{userId}`

Clear the whole basket. Source: `ClearBasketCommand`.

**204** on success — no body, **always**, whether or not a basket was stored. There is no 404 here.

| Status | `errorCode` | When |
|---|---|---|
| 503 | `Basket.OperationFailed` | Redis is unreachable (from source; not forced live) |

### `POST /api/v1/basket/{userId}/checkout`

Check the basket out: re-verify every line against Catalog, then hand it to Ordering asynchronously. Source:
`CheckoutBasketCommand`.

**Body** [`CheckoutRequest`](#checkoutrequest):

| Field | Type | Required | Notes |
|---|---|---|---|
| `shippingAddress` | object \| `null` | yes (must be present and non-null) | `street` (3–150 chars: letters, digits, spaces, `. , - / #`), `city` (2–100: letters, spaces, `. ' -`), `state` (same pattern as `city`), `zipCode` (3–12 characters; **12345** or **12345-6789** specifically when `country` is `US`, case-insensitive), `country` (exactly 2 letters, ISO 3166-1 alpha-2) |

There is no `paymentMethod` — Payment chooses it when the payment intent is created, later in the flow.

**200** [`CheckoutResponse`](#checkoutresponse): `{ "checkoutId": "<GUID>" }`.

- **Every line is re-read from Catalog first**, using the same anonymous, unauthenticated internal call
  [add](#post-apiv1basketuseriditems) uses. A line whose product is now gone from the public catalog, short of
  stock, or repriced blocks the whole checkout with **409** and a `lines` array (below) — nothing is ordered, and
  **the basket takes Catalog's current prices before answering**, so the next `GET` (and the next checkout attempt)
  shows what the customer would actually be charged.
- **A repeated request returns the same `checkoutId`.** Only when the basket is now **gone** and a completed-checkout
  marker for this user still exists (kept 24 hours) is a request treated as a retry of an already-successful
  checkout; an existing basket is always checked out as new, however soon after a previous checkout.
- Checking out an empty (or never-created) basket is `Basket.Empty`, not a silent no-op.

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `shippingAddress` missing or `null` (`"ShippingAddress: Shipping address is required"`); any address field fails its pattern — every failing field's message is joined in one `detail` with `"; "` |
| 400 | `Basket.Empty` | No stored basket, or a stored basket with no items, and no completed-checkout marker to repeat |
| 409 | `Basket.CheckoutRevalidationFailed` | One or more lines no longer match the catalog (see `lines`, below) |
| 409 | `Basket.CheckoutInProgress` | A checkout for this user is already running (3-minute internal lock) |
| 409 | `Basket.CheckoutConflict` | The stored basket changed between the read and the atomic commit, and there is no completed-checkout marker to explain it as a retry |
| 503 | `Basket.ProductVerificationFailed` | Catalog was unreachable while re-reading the lines — nothing is ordered |

Captured live — one basket with three problem lines at once (a product unpublished, one repriced, one short of
stock):

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Conflict","status":409,
 "detail":"Some items in the basket changed in the catalog. Review them and check out again.",
 "errorCode":"Basket.CheckoutRevalidationFailed",
 "traceId":"00-2f1a25da827a5263c99e00cf64ec1178-d23832a45b8353ca-00",
 "lines":[
   {"productId":"4354bb05-18fa-4fc9-aa37-01be9d36f1f5","reason":"Repriced","requestedQuantity":2,
    "basketPrice":50.00,"availableQuantity":20,"catalogPrice":75.00},
   {"productId":"c7a3c53f-255a-4de6-ae74-c6483a3796af","reason":"Unavailable","requestedQuantity":2,
    "basketPrice":20.00,"availableQuantity":null,"catalogPrice":null},
   {"productId":"fd54cab0-2ffe-4193-9136-76bea253322d","reason":"OutOfStock","requestedQuantity":2,
    "basketPrice":10.00,"availableQuantity":1,"catalogPrice":10.00}
 ]}
```

`reason` is one of `Unavailable` (gone from the public catalog: deleted, unpublished or discontinued —
`availableQuantity`/`catalogPrice` are `null`), `OutOfStock` (`availableQuantity` is Catalog's current stock, less
than `requestedQuantity`) or `Repriced` (Catalog's price no longer matches `basketPrice`; the basket has already
been updated to `catalogPrice` by the time this response arrives). Confirmed live: after fixing all three products
(republish, restock, and the price the basket already adopted), the same request with the same body then answered
200 with a fresh `checkoutId`.

Once Ordering has an order for this checkout, there is no further status here to poll — see
[flows.md](flows.md) for the checkout-to-paid sequence across services.

---

## Admin panel

Read-only: an admin sees carts and the outbox, and changes only the outbox (by replaying dead letters). No basket
content is ever written by these endpoints.

### Stored baskets

#### `GET /api/v1/basket/admin/carts`

Every stored basket, a page at a time. **Gateway:** `Admin` role. **Service:** permission `baskets.read`. Source:
`GetBasketsQuery`.

**Query** [`BasketScanQuery`](#basketscanquery); both optional:

| Parameter | Type | Default | Limit |
|---|---|---|---|
| `cursor` | string | — | Must be a `nextCursor` value this API returned, or omitted for the first page |
| `pageSize` | integer | `20` | 1–100 |

**200** [`AdminBasketPageDto`](conventions.md#63-basket-admin-scan-page-adminbasketpagedto). Captured live, one
stored basket:

```json
{"items":[{"userId":"0749b287-0897-408f-b248-5f133aab1796","isReadable":true,"lines":1,"totalItems":2,
  "totalPrice":84.00,"currency":"USD","createdAt":"2026-09-25T09:59:20.5574304Z",
  "lastModifiedAt":"2026-09-25T09:59:20.5574379Z"}],
 "nextCursor":null,"modifiedBefore":null}
```

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `pageSize` outside 1–100 (`"PageSize: pageSize must be between 1 and 100."`); `cursor` is not a value this API issued (`"Cursor: cursor must be a nextCursor value this API returned."`) |
| 503 | `Basket.OperationFailed` | Redis is unreachable (from source; not forced live) |

- **A page can be short, or even empty, while `nextCursor` is still set.** Each request scans a bounded number of
  Redis keys, not a bounded number of baskets. Keep requesting with the returned `nextCursor` until it is `null`.
- `isReadable: false` marks a stored document the domain rejects on read (corrupted, or predating a since-added
  limit); every other field on that entry is then `null`. The lines themselves are one call away:
  [`GET /api/v1/basket/{userId}`](#get-apiv1basketuserid), which an admin may always read.

### Abandoned baskets

#### `GET /api/v1/basket/admin/abandoned`

Stored baskets nobody has changed for at least a given age. **Gateway:** `Admin` role. **Service:** permission
`baskets.read`. Source: `GetAbandonedBasketsQuery`.

**Query**: `cursor`, `pageSize` as above, plus:

| Parameter | Type | Default | Limit |
|---|---|---|---|
| `olderThan` | string | `"24h"` | A whole positive number and a unit — `m` minutes, `h` hours or `d` days (`"90m"`, `"24h"`, `"3d"`); at most 30 days |

**200** [`AdminBasketPageDto`](conventions.md#63-basket-admin-scan-page-adminbasketpagedto), with `modifiedBefore`
set to the UTC cutoff actually applied (never `null` on this endpoint). Captured live, default cutoff, no basket
old enough yet:

```json
{"items":[],"nextCursor":null,"modifiedBefore":"2026-09-24T09:59:00.370317Z"}
```

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `pageSize`/`cursor` as above; `olderThan` is not the required form, or exceeds 30 days (`"OlderThan: olderThan must be a whole number of minutes, hours or days (90m, 24h, 3d), at most 30 days."`) |
| 503 | `Basket.OperationFailed` | Redis is unreachable (from source; not forced live) |

- "Changed" is the basket's own last-modified time — a re-price from a Catalog price-change event counts as a
  change, the same as a customer editing a line.
- An unreadable basket (`isReadable: false`) has no age to judge and is **never** listed here; it only ever appears
  under [`/carts`](#stored-baskets).
- There is no secondary index by age: this walks the same keys `/carts` does and filters. Expect short pages, or
  several empty ones with a non-null `nextCursor`, when few baskets qualify.

### Outbox dead letters

A dead letter is a checkout whose integration event could not be published after retrying for hours — an order
Ordering never received. **Gateway:** `Admin` role for all three endpoints below.

#### `GET /api/v1/basket/admin/outbox/dead-letters/details`

The dead letters themselves, newest first. **Service:** permission `system.manage`. Source:
`GetOutboxDeadLettersQuery`.

**Query**: `offset` (integer, default `0`, ≥ 0), `limit` (integer, default `20`, 1–100).

**200** [`OutboxDeadLetterPageDto`](conventions.md#64-dead-letter-page-outboxdeadletterpagedto). Captured live, none
dead-lettered:

```json
{"total":0,"offset":0,"limit":20,"items":[]}
```

| Status | `errorCode` | When |
|---|---|---|
| 400 | `Validation.Failed` | `offset` < 0 (`"Offset: 'Offset' must be greater than or equal to '0'."`); `limit` outside 1–100 |
| 503 | `Basket.OperationFailed` | Redis is unreachable (from source; not forced live) |

- This is a Redis **list** read (`LRANGE`), offset-paged rather than cursor-paged: a replay or a new dead letter
  between two page reads can shift the list, so a page can repeat or skip an entry. `total` — the whole list's
  current length — is the way to notice.
- An entry's shape (from source; none observed populated this stage): `isReadable` (false for a document the reader
  cannot parse — every other field is then `null`), `messageId` (the checkout's own id — the same `checkoutId` the
  customer was given), `eventType`, `occurredOnUtc`, `deadLetteredAtUtc` (`null` for one dead-lettered before this
  field existed), `attempts` (`null` if never actually attempted), `error` (`PublishFailed` \| `PublishTimedOut` \|
  `Unpublishable`), `exceptionType` (the exception's type name only — never its message, which can name hosts or
  users) and `correlationId`.

#### `GET /api/v1/basket/admin/outbox/dead-letters`

A **count only** — the legacy form this admin surface started with. **Service:** `Admin` role (not yet converted to
a permission, unlike the endpoint above; see F-09).

**200**: `{ "count": number }` — an anonymous object with no declared schema (F-10), confirmed live:

```json
{"count":0}
```

No error responses are declared or were observed.

#### `POST /api/v1/basket/admin/outbox/dead-letters/replay`

Replay dead letters back onto the outbox for another publish attempt. **Service:** `Admin` role (F-09).

**200**: `{ "replayed": number }` — again an anonymous object (F-10), confirmed live with nothing to replay:

```json
{"replayed":0}
```

- Replays **at most 1000** per call, oldest first; call again for more.
- No request body. No error responses are declared or were observed.

---

## Types

C# sources: `EShop.Basket.Application/Commands/*`, `Queries/GetBasket/BasketDto.cs`, `Queries/Admin/*`,
`EShop.Basket.API/Endpoints/*`. Every timestamp is UTC with `Z`. Money is a JSON number with two decimals, always
USD ([conventions.md §2](conventions.md#money)) — there is no `currency` field on a basket's own items, only on the
admin summary (`AdminBasketSummaryDto`).

### Storefront: requests

#### AddItemRequest

| Field | Type | Required | Notes |
|---|---|---|---|
| `productId` | string (GUID) | yes | |
| `quantity` | integer | yes | 1–999 |

#### UpdateQuantityRequest

| Field | Type | Required | Notes |
|---|---|---|---|
| `quantity` | integer | yes | 0–999. `0` removes the line |

#### CheckoutRequest

| Field | Type | Required | Notes |
|---|---|---|---|
| `shippingAddress` | [`CheckoutAddress`](#checkoutaddress) \| `null` | yes, non-null | |

#### CheckoutAddress

| Field | Type | Required | Notes |
|---|---|---|---|
| `street` | string | yes | 3–150 characters: letters, digits, spaces, `. , - / #` |
| `city` | string | yes | 2–100 characters: letters, spaces, `. ' -` |
| `state` | string | yes | Same pattern as `city` |
| `zipCode` | string | yes | 3–12 characters; when `country` is `US` (any case), must additionally be `12345` or `12345-6789` |
| `country` | string | yes | Exactly 2 letters (ISO 3166-1 alpha-2), e.g. `US` |

### Storefront: responses

#### Basket

Source: `BasketDto`.

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `userId` | string | no | |
| `items` | [`BasketItem`](#basketitem)[] | no | `[]` for an empty basket |
| `totalPrice` | number | no | Sum of every line's `subTotal` |
| `totalItems` | number | no | Sum of every line's `quantity` |
| `createdAt` | string (date-time) | yes | `null` only when there is no stored basket at all |
| `lastModifiedAt` | string (date-time) | yes | `null` only when there is no stored basket at all |

#### BasketItem

Source: `BasketItemDto`.

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `productId` | string (GUID) | no | |
| `productName` | string | no | Catalog's name at the time this line was added or last refreshed |
| `price` | number | no | The **effective** price (`discountPrice ?? price`) Catalog reported when this line was added or last refreshed — not necessarily today's |
| `quantity` | number | no | |
| `subTotal` | number | no | `price * quantity` |

#### CheckoutResponse

`{ "checkoutId": string (GUID) }` — the integration event's own id; Ordering deduplicates on it, and it is safe to
retry the same checkout with it.

#### CheckoutRevalidationProblem

The `409 Basket.CheckoutRevalidationFailed` body: every field of [conventions.md's `ProblemDetails`](conventions.md#3-errors),
plus:

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `lines` | [`CheckoutLineProblem`](#checkoutlineproblem)[] | no | One entry per line that failed revalidation |

#### CheckoutLineProblem

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `productId` | string (GUID) | no | |
| `reason` | `"Unavailable"` \| `"OutOfStock"` \| `"Repriced"` | no | |
| `requestedQuantity` | number | no | What the basket line asked for |
| `basketPrice` | number | no | What the basket charged **before** this response |
| `availableQuantity` | number | yes | Catalog's current stock; `null` when `reason` is `Unavailable` |
| `catalogPrice` | number | yes | Catalog's current price; `null` when `reason` is `Unavailable`. The basket has already adopted this value by the time the response arrives |

### Admin: requests

#### BasketScanQuery

The shared paging surface of [`/admin/carts`](#stored-baskets) and [`/admin/abandoned`](#abandoned-baskets):
`cursor` (string, optional), `pageSize` (integer, optional, default 20, 1–100).

#### AbandonedBasketsQuery

`BasketScanQuery`'s fields, plus `olderThan` (string, optional, default `"24h"`, at most 30 days).

#### OutboxDeadLettersQuery

`offset` (integer, optional, default 0, ≥ 0), `limit` (integer, optional, default 20, 1–100).

### Admin: responses

#### AdminBasketSummaryDto

One stored basket, summarised. Source: `AdminBasketSummaryDto`.

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `userId` | string | no | |
| `isReadable` | boolean | no | `false` for a document the domain rejects on read; every field below is then `null` |
| `lines` | number | yes | Distinct products |
| `totalItems` | number | yes | Summed quantity |
| `totalPrice` | number | yes | |
| `currency` | string | yes | Always `"USD"` when present |
| `createdAt` | string (date-time) | yes | |
| `lastModifiedAt` | string (date-time) | yes | |

#### AdminBasketPageDto

See [conventions.md §6.3](conventions.md#63-basket-admin-scan-page-adminbasketpagedto): `items`
([`AdminBasketSummaryDto`](#adminbasketsummarydto)[]), `nextCursor` (string \| null), `modifiedBefore`
(string (date-time) \| null — only ever set by `/abandoned`).

#### OutboxDeadLetterDto

One dead letter. Source: `OutboxDeadLetterDto`. From source; no populated entry was observed this stage.

| Field | Type | Nullable | Notes |
|---|---|---|---|
| `isReadable` | boolean | no | `false` for an entry that is not a readable outbox envelope; every field below is then `null` |
| `messageId` | string (GUID) | yes | The checkout's own id (the `checkoutId` the customer received) |
| `eventType` | string | yes | |
| `occurredOnUtc` | string (date-time) | yes | When the checkout happened |
| `deadLetteredAtUtc` | string (date-time) | yes | `null` for an entry dead-lettered before this field existed |
| `attempts` | number | yes | `null` for one never attempted (`Unpublishable`) or not recorded |
| `error` | `"PublishFailed"` \| `"PublishTimedOut"` \| `"Unpublishable"` | yes | |
| `exceptionType` | string | yes | The exception's type name only, never its message |
| `correlationId` | string | yes | |

#### OutboxDeadLetterPageDto

See [conventions.md §6.4](conventions.md#64-dead-letter-page-outboxdeadletterpagedto): `total` (number, the whole
list's length), `offset` (number), `limit` (number), `items` ([`OutboxDeadLetterDto`](#outboxdeadletterdto)[]).

### TypeScript

`ProblemDetails` is in [conventions.md](conventions.md#3-errors).

```ts
// ---- Storefront: requests ----

export interface AddItemRequest {
  productId: string;
  /** 1-999. */
  quantity: number;
}

export interface UpdateQuantityRequest {
  /** 0-999. 0 removes the line. */
  quantity: number;
}

export interface CheckoutAddress {
  /** 3-150 chars: letters, digits, spaces, . , - / # */
  street: string;
  /** 2-100 chars: letters, spaces, . ' - */
  city: string;
  /** Same pattern as city. */
  state: string;
  /** 3-12 chars; 12345 or 12345-6789 specifically when country is US. */
  zipCode: string;
  /** ISO 3166-1 alpha-2, e.g. US. */
  country: string;
}

export interface CheckoutRequest {
  shippingAddress: CheckoutAddress;
}

// ---- Storefront: responses ----

export interface BasketItem {
  productId: string;
  productName: string;
  /** The effective price (discountPrice ?? price) as of this line's last add/refresh. */
  price: number;
  quantity: number;
  subTotal: number;
}

export interface Basket {
  userId: string;
  items: BasketItem[];
  totalPrice: number;
  totalItems: number;
  createdAt: string | null;
  lastModifiedAt: string | null;
}

export interface CheckoutResponse {
  checkoutId: string;
}

export type CheckoutLineReason = 'Unavailable' | 'OutOfStock' | 'Repriced';

export interface CheckoutLineProblem {
  productId: string;
  reason: CheckoutLineReason;
  requestedQuantity: number;
  basketPrice: number;
  /** null when reason is Unavailable. */
  availableQuantity: number | null;
  /** null when reason is Unavailable. The basket has already adopted this price. */
  catalogPrice: number | null;
}

/** The 409 Basket.CheckoutRevalidationFailed body. */
export interface CheckoutRevalidationProblem extends ProblemDetails {
  lines: CheckoutLineProblem[];
}

// ---- Admin ----

export interface BasketScanQuery {
  /** A nextCursor value this API returned; omit for the first page. */
  cursor?: string;
  /** Default 20, 1-100. */
  pageSize?: number;
}

export interface AbandonedBasketsQuery extends BasketScanQuery {
  /** "90m" | "24h" | "3d" form. Default "24h", max 30 days. */
  olderThan?: string;
}

export interface AdminBasketSummaryDto {
  userId: string;
  isReadable: boolean;
  lines: number | null;
  totalItems: number | null;
  totalPrice: number | null;
  currency: string | null;
  createdAt: string | null;
  lastModifiedAt: string | null;
}

export interface AdminBasketPageDto {
  items: AdminBasketSummaryDto[];
  nextCursor: string | null;
  /** Set only by /abandoned. */
  modifiedBefore: string | null;
}

export type OutboxDeadLetterError = 'PublishFailed' | 'PublishTimedOut' | 'Unpublishable';

export interface OutboxDeadLetterDto {
  isReadable: boolean;
  messageId: string | null;
  eventType: string | null;
  occurredOnUtc: string | null;
  deadLetteredAtUtc: string | null;
  attempts: number | null;
  error: OutboxDeadLetterError | null;
  exceptionType: string | null;
  correlationId: string | null;
}

export interface OutboxDeadLetterPageDto {
  total: number;
  offset: number;
  limit: number;
  items: OutboxDeadLetterDto[];
}

export interface OutboxDeadLetterCount {
  count: number;
}

export interface OutboxReplayResult {
  replayed: number;
}
```

---

## Frontend notes

> ⚠ **There is no anonymous basket.** Every route needs a signed-in caller; design the storefront so "add to
> basket" always has a user id to send, not a guest session to merge later.

> ⚠ **The owner check is case-sensitive.** Send the `{userId}` exactly as the login response gave it. A client that
> normalises case (or copies an id from somewhere that did) gets 403 on its own basket.

> ⚠ **`DELETE /items/{productId}` is 404, not 204, when the caller has no stored basket at all** — only 204 when a
> basket exists but does not hold that product. A client treating every "remove" 404 as "already gone, ignore it"
> will do so correctly here, but should not assume the route is unconditionally idempotent the way
> `DELETE /{userId}` (clear) is.

> ⚠ **`price` on a basket line is a snapshot, not a live quote.** It is Catalog's effective price
> (`discountPrice ?? price`) at the moment the line was added or last merged — a price change afterwards is not
> reflected until the next add of that product, or until checkout revalidates and takes the new price after
> refusing. Do not treat a basket's displayed total as guaranteed until after checkout.

> ⚠ **A 409 `Basket.CheckoutRevalidationFailed` already changed the basket.** The response's `lines` describe what
> was wrong, but by the time the client reads it, `Repriced` lines have already been updated to the new price.
> Re-fetch the basket (or trust the `catalogPrice` in each line) before showing the customer what they will now pay.

> ⚠ **A repeated checkout call is not "checked out twice."** If the basket is gone and the previous checkout is
> still within its 24-hour window, the same request returns the same `checkoutId`. Retrying a checkout the client
> is unsure about is safe.

> ⚠ **The two legacy outbox endpoints return undeclared, anonymous response shapes** — `{ count }` and
> `{ replayed }` are not in the OpenAPI document, and both still require the `Admin` role rather than a permission,
> unlike every other admin endpoint here. (F-09, F-10)

---

## Related documents

- [conventions.md](conventions.md): errors, paging, rate limits, auth
- [flows.md](flows.md): browse → basket → checkout → order appears → payment
- [ordering.md](ordering.md): what happens to a checkout on the other side

---

**Version**: 1.0  
**Last Updated**: 2026-09-25
