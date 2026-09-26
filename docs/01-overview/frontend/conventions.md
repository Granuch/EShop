# Conventions shared by every endpoint

The rules in this file apply to every service. The per-service files ([README](README.md#file-map)) only record where
an endpoint departs from them.

**Verified at:** `105d647` (`feature/admin-panel`, 2026-09-23); [§7](#7-rate-limits) was re-verified at `0e1bc06`, and
the date rule in [§2](#dates-and-times) and Identity's binding 400 in [§3.3](#33-validation-errors) were corrected at
`1fcb630`; the login-throttle sentence in §7 was updated at `bb8c148`; Ordering's row in
[§10](#enum-filters) was corrected at `530fe5d`; Payment's malformed-query note in §2 and the delays in
[§9](#9-consistency-and-caching) were updated at `0b7f826`; [§3.2](#32-responses-with-no-body) (the 504 row),
[§3.4](#34-errorcode-values) (`MalformedRequest`) and [§6.5](#65-audit-page-gatewayauditlogpagedto) were updated at
`0d87f3b`. Malformed requests ([§2](#unknown-and-malformed-request-bodies), [§3.4](#34-errorcode-values)), the
no-body list ([§3.2](#32-responses-with-no-body)), 429s ([§3.5](#35-recommended-client-handling),
[§7](#7-rate-limits)) and CORS ([§8](#cors), [§11](#11-csv-downloads)) were re-verified at `5980146`, which fixed
F-04, F-05 and F-20. Enums ([§2](#enums), [§10](#enum-filters), [§11](#11-csv-downloads)) were re-verified at
`30e1221`, which fixed F-01. Permissions ([§4](#claims-and-roles), [§5](#5-permissions-and-admin-access)), the role
lists ([§6](#6-paging)) and Identity's dates ([§10](#dates-always-send-z)) were re-verified at `e2ec172`, which fixed F-07,
F-12 and F-15.
Every rule here was checked against the source and observed live through the gateway on the docker compose
`sandbox` stack. Where the code and this file disagree, the
code wins; see [README](README.md#status).

---

## Contents

1. [Base URL and routing](#1-base-url-and-routing)
2. [JSON](#2-json)
3. [Errors](#3-errors)
4. [Authentication](#4-authentication)
5. [Permissions and admin access](#5-permissions-and-admin-access)
6. [Paging](#6-paging)
7. [Rate limits](#7-rate-limits)
8. [Headers and CORS](#8-headers-and-cors)
9. [Consistency and caching](#9-consistency-and-caching)
10. [Query parameters](#10-query-parameters)
11. [CSV downloads](#11-csv-downloads)
12. [Infrastructure endpoints](#12-infrastructure-endpoints)

---

## 1. Base URL and routing

A client talks to **one origin, the API gateway**:

| Environment | Base URL |
|---|---|
| Local compose stack (`docker compose --profile sandbox up -d --build`) | `http://localhost:7000` |

The gateway (YARP) **does not rewrite paths**. The path you call on the gateway is the path the service serves, so
`GET http://localhost:7000/api/v1/products` reaches Catalog's `/api/v1/products`. Every API path starts with
`/api/v1/`.

### Gateway route table

A route's policy is the gateway's half of the authorization check. Each service then applies its own policy again; the
service files list both. `{**}` also matches the bare prefix (for example `/api/v1/products` itself).

| Path | Methods | Service | Gateway policy |
|---|---|---|---|
| `/api/v1/auth/{**}` | all | Identity | anonymous |
| `/api/v1/account/{**}` | all | Identity | signed in |
| `/api/v1/admin/users/{**}` | all | Identity | `Admin` role |
| `/api/v1/roles/{**}` | all | Identity | `Admin` role |
| `/api/v1/products/deleted`, `/api/v1/products/export` | GET, HEAD | Catalog | `Admin` role |
| `/api/v1/products/{**}`, `/api/v1/categories/{**}` | GET, HEAD, OPTIONS | Catalog | anonymous |
| `/api/v1/products/{**}`, `/api/v1/categories/{**}` | POST, PUT, PATCH, DELETE | Catalog | `Admin` role |
| `/api/v1/admin/catalog/{**}`, `/api/v1/admin/cache/{**}` | all | Catalog | `Admin` role |
| `/api/v1/basket/admin/{**}` | all | Basket | `Admin` role |
| `/api/v1/basket/{**}` | all | Basket | signed in |
| `/api/v1/orders/{**}` | all | Ordering | signed in |
| `/api/v1/users/{userId}/orders/{**}` | GET, HEAD, OPTIONS | Ordering | signed in |
| `/api/v1/payments/{**}` | all | Payment | signed in |
| `/api/v1/users/{userId}/payments` | all | Payment | signed in |
| `/api/v1/notifications/{**}` | all | Notification | `Admin` role |
| `/api/v1/admin/audit` | GET | the gateway itself | `audit.read` permission |
| `/api/v1/admin/health`, `/api/v1/admin/settings`, `/api/v1/admin/feature-flags` | GET | the gateway itself | `system.manage` permission |

The last two rows are served by the gateway rather than proxied; they are documented in
[admin-platform.md](admin-platform.md).

### Not reachable through the gateway

A path that matches no route gets a **bare `404` with an empty body** from the gateway. This is not a problem+json
response. These service paths exist but are deliberately not routed:

| Path | Why |
|---|---|
| `POST /webhooks/stripe` (Payment) | Called by Stripe, server to server |
| `GET /api/v1/users/{userId}/contact` (Identity) | Internal, needs a service API key |
| `GET /api/v1/admin/audit`, `/api/v1/admin/settings`, `/api/v1/admin/feature-flags` on a service | The gateway serves its own merged version at the same path |
| Each service's `/`, `/health*`, `/openapi/v1.json`, `/scalar/v1` | Infrastructure; see [§12](#12-infrastructure-endpoints) |

> ⚠ **Identity's OpenAPI document lists PascalCase paths** (`/api/v1/Auth/login`, `/api/v1/Account/profile`), because
> its controllers use `[Route("api/v1/[controller]")]`. Routing is case-insensitive, so both forms work. These docs use
> lowercase, like every other service; do not copy the casing from a generated client. (F-19)

---

## 2. JSON

### Property names and nulls

- Responses use **camelCase** property names (`stockQuantity`, `createdAt`).
- A property whose value is null is **written out as `null`**, never omitted. Type it as `T | null`, not `T?`.
- Request property names are matched **case-insensitively**. `"NAME"` binds to `name`, even in Catalog. Send camelCase
  anyway.

### Unknown and malformed request bodies

The services disagree about unknown properties, so the client must never send extra ones.

| Service | Unknown property in the body | Body that is not valid JSON |
|---|---|---|
| Catalog | **400** `MalformedRequest`, `detail` names the JSON path (`'$.bogusField'`) | 400 `MalformedRequest` |
| Ordering, Notification, Basket, Payment | ignored | 400 `MalformedRequest` |
| Identity | ignored | 400 `ValidationError`, keyed `$` ([§3.3](#33-validation-errors)) |

A query value of the wrong type (`?pageSize=abc`, `?from=yesterday`) is also 400 `MalformedRequest` everywhere except
Identity, including the gateway's own endpoints. So is a missing body where one is required.

> ⚠ Only Catalog rejects unknown properties, so a request that works against Ordering can fail against Catalog.
> (F-02)

### Identifiers

- Entity ids are **GUID strings**, lowercase with hyphens (`"9316561d-b960-46e2-862d-b7aceea4b77d"`).
- User ids (`userId`, `actorUserId`) are Identity's user id. It is also a GUID string, but some services type it as a
  plain string, so compare it as a string.
- The one numeric id is an audit entry's `id`, a JSON integer.
- A route segment typed `{id:guid}` that is not a GUID never reaches the service. The route does not match and the
  service answers a **bare `404` with an empty body**. A well-formed but unknown id gets the service's own
  problem+json 404 (for example `Product.NotFound`).

### Dates and times

- Timestamps are **ISO-8601 in UTC with a `Z` suffix**: `"2026-09-16T10:55:12.139241Z"`.
- ⚠ The exception is a `DateTimeOffset` field. It is written with a `+00:00` offset and whole seconds:
  `"2026-09-23T18:59:25+00:00"`. Seen so far on Identity's `lockoutEnd`; each service file marks its own. (F-35)
- The number of fractional digits varies, because trailing zeros are dropped (`"…52.88013Z"`). Parse with
  `new Date(value)`; never with a fixed-length pattern.
- Date-only values do not exist in responses.
- For query parameters, see the `Z` rule in [§10](#10-query-parameters).

### Money

- Amounts are **JSON numbers** with two decimal places, stored as `decimal(18,2)`. `"price": 42.00` parses to `42` in
  JavaScript.
- **Everything is in USD.** Only three DTOs carry an explicit `currency` field, and it is always `"USD"`:
  - Payment's `PaymentDto`;
  - Payment's `PaymentStatsDto`;
  - Basket's admin basket summary.

  Other amounts (product prices, order totals) carry no currency field, and are USD.
- Format for display with `Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })`.
- The server computes totals, discounts and effective prices. Display them; do not recompute them.

### Enums

**Every enum is sent as its PascalCase name**, by every service, in JSON and in the CSV exports alike. Each service
file repeats its own enums as a TypeScript string-literal union. The main ones:

| Enum | Where | Values |
|---|---|---|
| `ProductStatus` | Catalog `status` | `"Draft"` · `"Active"` · `"Discontinued"` |
| `OrderStatus` | Ordering `status`, `fromStatus`, `toStatus` | `"Pending"` · `"Paid"` · `"Shipped"` · `"Delivered"` · `"Cancelled"` · `"Refunded"` |
| `PaymentStatus` | Payment `status`, `fromStatus`, `toStatus` | `"Pending"` · `"Processing"` · `"Success"` · `"Failed"` · `"Refunded"` · `"Cancelled"` |
| `PaymentMethodType` | Payment `paymentMethod` | for example `"Stripe"`; full list in [payment.md](payment.md) |
| `NotificationStatus` | Notification `status` | `"Pending"` · `"Sent"` · `"Failed"` · `"Sending"` · `"Undeliverable"` |
| `AuditOutcome` | Audit `outcome` | `"Succeeded"` · `"Rejected"` · `"Failed"` |

A query parameter takes the same name in any case and refuses a number ([§10](#enum-filters)), so a value read from a
response can be sent straight back.

Three strings look like enums and follow other rules: create-intent's `status` is **Stripe's** lower-case vocabulary
(`"requires_payment_method"`), a notification's template name is lower-case kebab (`"order-created"`), and the merged
audit's `service` is lower case (`"catalog"`).

> Until 2026-09-26 (frontend-contracts F-01), Catalog and Ordering sent integers (`"status": 1`) and Payment sent
> upper-case statuses (`"SUCCESS"`). Code written against those forms must be updated.

---

## 3. Errors

### 3.1 The envelope

Every error the services write themselves is **RFC 7807 problem+json** (`Content-Type: application/problem+json`;
Identity adds `; charset=utf-8`). Captured from `GET /api/v1/products/00000000-0000-0000-0000-00000000abcd`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "Product with ID '00000000-0000-0000-0000-00000000abcd' was not found.",
  "errorCode": "Product.NotFound",
  "traceId": "00-634bdf422dd5835188a8becdae249927-13c7138253f5ebb4-00"
}
```

| Field | Type | Present | Meaning |
|---|---|---|---|
| `type` | string | not on Identity's command failures | A link to the HTTP status in RFC 9110. Carries no application meaning |
| `title` | string | not on Identity's command failures | The status reason phrase |
| `status` | number | always | Same as the HTTP status |
| `detail` | string | almost always | A human-readable reason in English. Safe to show, but do not parse it |
| `errorCode` | string | always (in every problem body captured) | **The machine-readable discriminator. Branch on this** |
| `traceId` | string | always | An opaque id for support. Its format differs between Identity and the other services |
| `errors` | `Record<string, string[]>` | `ValidationError` only | Per-field messages, keyed by camelCase field name; see [§3.3](#33-validation-errors) |
| other members | any | per endpoint | A few endpoints add their own member, such as Basket's checkout `lines`. Each is documented with its endpoint |

Identity's controller failures (login, refresh, account, roles, admin users) omit `type` and `title`, and use a
different `traceId` format. Captured from a replayed refresh token:

```json
{"status":401,"detail":"Invalid or expired refresh token","errorCode":"Auth.InvalidToken","traceId":"0HNOPIAQHPUF8:00000002"}
```

> ⚠ Do not require `type` or `title`: Identity leaves them out. Treat `traceId` as an opaque string. (F-21)

### 3.2 Responses with no body

These responses carry **no body at all**. Handle them from the status code alone:

| Status | When |
|---|---|
| 401 | No token, or an invalid or expired one, on a protected route. The header is `WWW-Authenticate: Bearer`, plus `error="invalid_token"` when a token was sent. The gateway and the services answer the same way |
| 403 | Signed in, but a role or permission is missing, at either layer |
| 404 | No gateway route matches, or a `{id:guid}` segment is not a GUID |
| 502 | Payment is unreachable; the gateway does not rewrite Payment's 502 (F-13) |
| 504 | The service did not answer within the gateway's 10 s (every service except Payment, whose route keeps YARP's default of 100 s). Observed on Notification's resend and retry-failed; see [notification.md](notification.md#frontend-notes) (F-56) |

### 3.3 Validation errors

A request that fails validation answers **400** with `errorCode` **`ValidationError`**, in one shape everywhere:

- **`errors`** maps each field to its messages. A key is the field's **camelCase wire name**, the same name the client
  sent (`pageSize`, `name`, `zipCode`; a nested or indexed field reads `items[0].quantity`). A rule about the request as
  a whole rather than one field is keyed **`$`** — the audit page's "`from` must be earlier than `to`", Identity's
  user date filters and Catalog's stock rule ("supply either `delta` or `absolute`") are. Ordering's, Payment's and
  Notification's date filters report their "`to` before `from`" check under **`to`** instead.
- **`detail`** lists every message once, joined by `"; "`, for a form-level summary.

Captured from `GET /api/v1/products?pageSize=101`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Page size must not exceed 100",
  "errorCode": "ValidationError",
  "traceId": "00-5551e5aca603ac2c5c57c98637abe627-8066439ad26fa657-00",
  "errors": {
    "pageSize": [
      "Page size must not exceed 100"
    ]
  }
}
```

Captured from `PUT /api/v1/categories/{id}` with an empty name:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Category name is required",
  "errorCode": "ValidationError",
  "traceId": "00-6ff336f5af398b6bd601611b088f05aa-d4c4cc19c00d5cae-00",
  "errors": {
    "name": [
      "Category name is required"
    ]
  }
}
```

Identity answers the same shape when a request cannot be bound at all: a body that is not JSON (keyed `$`, the JSON
root), a `null` for a required string, or a query value of the wrong type (`isActive=yes`, `sortBy=Bogus`). Its keys
can also name the bound parameter itself (`request`, `command`) alongside the field. Captured from
`POST /api/v1/account/change-password` with the body `{`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. Path: $ | LineNumber: 0 | BytePositionInLine: 1.; The request field is required.",
  "traceId": "00-e20000cf76c17841f3b57a27f530cb38-355461c0648e9c6f-00",
  "errorCode": "ValidationError",
  "errors": {
    "$": [
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. Path: $ | LineNumber: 0 | BytePositionInLine: 1."
    ],
    "request": [
      "The request field is required."
    ]
  }
}
```

To show errors on a form, match each key to the field's name; show `detail` (or any key that matches no field, such
as `$`) as a form-level message.

A few endpoints also send their own validation-like codes with no `errors` map, for a check made outside the
validator: `Validation.IdMismatch` (a body id that differs from the route's) and `Validation.IncompleteReorder`. Each is
documented with its endpoint.

### 3.4 `errorCode` values

**Shared codes**, produced by the common middleware:

| `errorCode` | Status | Meaning |
|---|---|---|
| `ValidationError` | 400 | Every validation failure, with an `errors` map ([§3.3](#33-validation-errors)) |
| `MalformedRequest` | 400 | The body is missing or not valid JSON, or has an unknown property (Catalog only). **Also** returned for a **query** value of the wrong type, such as `?status=active` or `?pageSize=abc`, by every component except Identity, with a `detail` that wrongly blames the request body (F-25) |
| `DomainError` | 400 | A business rule rejected the request; `detail` says which rule |
| `NotFound` | 404 | Generic not-found; most services use their own `*.NotFound` code instead |
| `Unauthorized` | 401 | Authentication was required inside a handler |
| `ConcurrencyConflict` | 409 | Another request changed the resource first. Reload and retry |
| `DuplicateResource` | 409 | A unique value is already taken |
| `InternalServerError` | 500 | Unexpected failure. `detail` is always `"An unexpected error occurred."` |
| `Request.RateLimited` | 429 | Rate limited, by the gateway or a service; see [§7](#7-rate-limits) |

**Codes the gateway generates itself:**

| `errorCode` | Status | Meaning |
|---|---|---|
| `Request.PayloadTooLarge` | 413 | The body is over **1 MiB**. Product import allows **8 MiB** |
| `Gateway.UpstreamUnavailable` | 503 | The service is unreachable. Sent with `Retry-After: 5` |
| `Gateway.SimulatedFailure` | 500/503 | Injected by the traffic simulator. Off unless the gateway runs in Development |

The 413 and 503 come from the gateway's per-service guards. `/api/v1/payments/**` and `/api/v1/users/{id}/payments`
have no guard, so there the body is not capped and an unreachable Payment gives a bare **502** (F-13). A 413 captured
from `POST /api/v1/categories`:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.14","title":"Content Too Large","status":413,
 "detail":"Request payload exceeds allowed size for catalog endpoints.","errorCode":"Request.PayloadTooLarge",
 "traceId":"00-e926a07e28334d02477018b0afda6f2a-5bc171413a200ae0-00"}
```

**Service codes** take the form `Service.Reason`: `Product.NotFound`, `Order.NotFound`, `Basket.ProductNotFound`,
`Auth.InvalidCredentials`. Each service file lists every code with its status.

> ⚠ Payment uses `SCREAMING_SNAKE` codes instead: `PAYMENT_NOT_FOUND`, `PAYMENT_NOT_READY`, `EXPORT_TOO_LARGE`. Do
> not assume the dotted form. (F-24)

### 3.5 Recommended client handling

1. **2xx**: parse the body (a 204 has none).
2. **401**:
   - on a request made with an access token, refresh once ([§4](#4-authentication)) and retry once;
   - if the refresh fails, sign the user out;
   - on `/auth/*`, the 401 is the answer itself (for example wrong credentials).
3. **403**: show "not allowed". Never retry.
4. **Empty body**: branch on the status alone ([§3.2](#32-responses-with-no-body)).
5. **problem+json**: branch on `errorCode`:
   - `ValidationError`: show `errors` on the matching fields, and `detail` at form level;
   - `*.NotFound`: show a not-found state;
   - 409 codes: reload, then offer a retry;
   - `Gateway.UpstreamUnavailable`: retry after 5 s with back-off;
   - anything else: show `detail` and keep `traceId` for support.
6. **429** (`Request.RateLimited`): back off for the `Retry-After` seconds, which every 429 carries and the browser
   can read cross-origin ([§8](#8-headers-and-cors)).

```ts
/**
 * RFC 7807 body as the services send it. `type`/`title` are missing on Identity's controller failures, and on a 429
 * `type` is missing everywhere. Endpoints that add members document an interface that extends this one.
 */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status: number;
  detail?: string;
  errorCode: string;
  traceId: string;
  /** `ValidationError` only. Keys are camelCase field names; "$" is a rule about the whole request. */
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails | null,
  ) {
    super(problem?.detail ?? problem?.title ?? `HTTP ${status}`);
  }

  /** Undefined only for the responses in §3.2, which have no body. */
  get errorCode(): string | undefined {
    return this.problem?.errorCode;
  }
}

/** Reads an error response. Several responses have no body at all (§3.2), so null is a normal result. */
export async function toApiError(response: Response): Promise<ApiError> {
  const text = await response.text();
  let problem: ProblemDetails | null = null;
  if (text && (response.headers.get('content-type') ?? '').includes('json')) {
    problem = JSON.parse(text) as ProblemDetails;
  }
  return new ApiError(response.status, problem);
}
```

---

## 4. Authentication

This section is an overview. The request and response contracts are in [identity.md](identity.md).

- **Bearer tokens.** Send `Authorization: Bearer <accessToken>` on every call that needs a user.
- **Invalid tokens on public endpoints are ignored.** On a public endpoint (the storefront catalog, `/auth/*`), an
  invalid or expired token is not rejected. The request runs as anonymous and you get the public view, not a 401. A
  401 therefore only ever comes from a protected route.

### Sign-in

| Step | Call | Result |
|---|---|---|
| Register | `POST /api/v1/auth/register` | Account created. The body is `{ userId, email, message }` |
| Log in | `POST /api/v1/auth/login` | `{ accessToken, refreshToken, expiresIn, tokenType, requires2FA, user }` |

- **Email confirmation is not in use.** The account can log in straight after registering.
  - Every shipped configuration (compose, k8s) sets `Identity:RequireConfirmedEmail=false`.
  - Identity refuses to start with it switched on, because no confirmation token is ever delivered.
  - `POST /api/v1/auth/confirm-email` exists, but a user has no way to obtain the token it needs. Only an admin can
    mark an email confirmed ([identity.md](identity.md)).
- **Two-factor login.** When the account has 2FA enabled and the login request has no `twoFactorCode`, login answers
  **200** with `requires2FA: true`, empty `accessToken`/`refreshToken`, and `user: null`. Ask the user for the
  authenticator code, then **send the same login request again** with `twoFactorCode` added. There is no separate 2FA
  endpoint for this step.
- **Local email.** In the local stack, the emails the platform does send (password reset, order and payment
  notifications) land in Mailpit at `http://localhost:8025`.

> ⚠ Registration's `message` reads "Registration successful. Please check your email to confirm.", but **no email is
> sent** (observed: nothing reached Mailpit within 20 s). Do not show that text, and do not build a "confirm your
> email" step into sign-up. (F-27)

### Tokens

| Token | Lifetime | Notes |
|---|---|---|
| Access token (JWT, HS256) | **60 minutes** (`expiresIn: 3600`, in seconds) | Validated with **zero clock skew** by the gateway and every service; it stops working exactly at `exp` |
| Refresh token (opaque string) | **7 days** | **Rotated on every use.** `POST /api/v1/auth/refresh-token` returns a new pair; the old refresh token is dead from then on |

- **Refresh.** Refresh shortly before `expiresIn` runs out, or on the first 401. Run only **one refresh at a time**: a
  second refresh with the same token fails.
- **Rejected refresh tokens.** A replayed, revoked or expired refresh token answers **401 `Auth.InvalidToken`**
  (observed). The source also has `Auth.TokenAlreadyUsed` for a rotation that loses a database race, but four parallel
  refreshes of one token all produced `Auth.InvalidToken`. Handle both codes the same way: sign the user out.
- **Log out.** `POST /api/v1/auth/revoke-token` with `{ "refreshToken": "…" }` answers **204**. The access token stays
  valid until it expires, so also drop it on the client.
- **Password changes.** Changing or resetting the password revokes every refresh token of that user
  ([identity.md](identity.md)).

### Claims and roles

The JWT carries:
- `sub` (user id), `email`, `firstName`, `lastName`, `jti`;
- `iss` `EShop.Identity` and `aud` `EShop.Services`;
- `exp`;
- the role under the long claim type `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`.

It has no `iat`, `nbf` or `permission` claims.

**Use `user.permissions` and `user.roles` from the login response**, not the decoded token. The role claim's type is
the long URI above, it is a string for one role but an array for several, and the token carries no permissions.
`GET /api/v1/account/profile` returns the same two lists ([§5](#5-permissions-and-admin-access)).

> **Token storage (recommendation for Next.js).**
> - Keep the access token in memory only.
> - Keep the refresh token out of reach of page scripts: an `httpOnly`, `Secure`, `SameSite=Strict` cookie set by a
>   Next.js route handler that calls `/auth/refresh-token` on the server.
> - Never put either token in `localStorage`.
>
> The gateway allows credentials from the configured origins ([§8](#8-headers-and-cors)), but the API itself never
> sets a cookie. Any cookie is the Next.js app's own.

---

## 5. Permissions and admin access

Admin endpoints are authorized at **two layers**:

1. **The gateway route policy.** This is anonymous, signed in, or the **`Admin` role** ([§1](#gateway-route-table)).
2. **The service policy.** This is the `Admin` role or a named **permission**, and it is checked again inside the service.

A request needs both. Each endpoint in the service files lists the two, for example
"gateway: `Admin` role · service: `notifications.read`".

### The permission vocabulary

There are 15 permissions. A caller holds a permission through a role bundle. **Today the only bundle is `Admin`, and
it holds all 15.** The default `User` role holds none.

**What the caller holds** is in `user.permissions` on the login response and in `permissions` on
`GET /api/v1/account/profile` ([identity.md](identity.md#post-apiv1authlogin)): each permission once, in the order of
the table below, and `[]` for a customer. Identity computes it from the caller's roles with the same table the services
authorize against, so show an admin screen exactly when it lists the permission that screen's endpoints need. The
access token itself carries no `permission` claim.

| Permission | Grants |
|---|---|
| `catalog.read` | Catalog data the public cannot see: drafts, soft-deleted products, stock reports |
| `catalog.write` | Create, change, publish, delete and restore products and categories |
| `orders.read` | List and read any order |
| `orders.write` | Ship, deliver, edit lines and shipping address, add notes |
| `payments.read` | List and read any payment and its event history |
| `payments.write` | Record a payment, including an offline one |
| `payments.refund` | Refund a payment |
| `users.read` | List and read user accounts, their roles and sessions |
| `users.manage` | Create, edit, lock, deactivate, delete and restore accounts |
| `roles.manage` | Create and delete roles, change membership |
| `notifications.read` | Read the notification journal |
| `notifications.manage` | Resend, retry, close notifications, send template tests |
| `baskets.read` | Read another user's basket |
| `audit.read` | Read the admin audit trail |
| `system.manage` | Health page, cache invalidation, feature flags, outbox replay, settings |

```ts
export type Permission =
  | 'catalog.read' | 'catalog.write'
  | 'orders.read' | 'orders.write'
  | 'payments.read' | 'payments.write' | 'payments.refund'
  | 'users.read' | 'users.manage' | 'roles.manage'
  | 'notifications.read' | 'notifications.manage'
  | 'baskets.read' | 'audit.read' | 'system.manage';
```

Not every admin endpoint uses a permission yet. Many still require the `Admin` **role** directly, including:
- Catalog's product and category administration;
- Ordering's admin endpoints;
- Identity's `/roles`;
- Payment's refund and settle endpoints. The service files
say which one each endpoint uses.

### What an admin screen needs

| Screen | Gateway | Service |
|---|---|---|
| Users | `Admin` role | `users.read`; writes add `users.manage`; setting roles adds `roles.manage` |
| Roles | `Admin` role | `Admin` role |
| Products, categories, low stock | `Admin` role (writes, `/deleted`, `/export`, `/admin/catalog`) | `Admin` role |
| Cache invalidation | `Admin` role | `system.manage` |
| Orders (list, stats, notes, history, ship, deliver) | signed in | `Admin` role |
| Payments (list, stats, export, events) | signed in | `payments.read` |
| Payments (offline settle, webhook replay) | signed in | `payments.write` |
| Payments (settle, refund, simulation) | signed in | `Admin` role |
| Baskets (carts, abandoned) | `Admin` role | `baskets.read` |
| Basket outbox | `Admin` role | `system.manage` (details) or `Admin` role (count, replay) |
| Notifications | `Admin` role | `notifications.read` / `notifications.manage` |
| Audit log | the gateway itself | `audit.read` |
| System (health, settings, flags) | the gateway itself | `system.manage` |

> ⚠ The gateway requires the `Admin` **role** on `/api/v1/notifications`, `/api/v1/basket/admin`,
> `/api/v1/admin/users`, `/api/v1/admin/catalog` and `/api/v1/admin/cache`. A future role granted only some
> permissions would still be refused there by the gateway. (F-08)

---

## 6. Paging

List endpoints use one of six response shapes. Each list endpoint in the service files names its shape, its defaults
and its limits.

### 6.1 `PagedResult<T>` (offset pages, the common case)

Used by the product, category-product, low-stock and deleted-product lists, the order lists, the payment lists, the
admin user list, the two role lists and the notification journal.

| Query parameter | Default | Limit |
|---|---|---|
| `pageNumber` | `1` | ≥ 1 |
| `pageSize` | `10`; `20` for admin users and notifications; `50` for the role lists | 1–100 |

A value out of range gets **400 `ValidationError`**, keyed by the parameter ([§3.3](#33-validation-errors)).

| Field | Type | Meaning |
|---|---|---|
| `items` | `T[]` | This page |
| `pageNumber` | number | Echoes the page that was served |
| `pageSize` | number | Echoes the size that was served |
| `totalCount` | number | Items across all pages |
| `totalPages` | number | `ceil(totalCount / pageSize)`; `0` when empty |
| `hasPreviousPage` | boolean | `pageNumber > 1` |
| `hasNextPage` | boolean | `pageNumber < totalPages` |

A page past the end is not an error. `?pageNumber=999` answers 200 with `items: []`, `pageNumber: 999`,
`hasPreviousPage: true`, and `totalCount` still counting the whole list.

### 6.2 `CursorPagedResult<T>` (forward cursor)

Used by `GET /api/v1/products/newest`. Pass `nextCursor` back as `?cursor=`. `pageSize` defaults to 10, with a
maximum of 100.

| Field | Type | Meaning |
|---|---|---|
| `items` | `T[]` | This page |
| `pageSize` | number | Echoed |
| `nextCursor` | string \| null | Opaque; `null` on the last page |
| `previousCursor` | string \| null | Always `null` today: the cursor only moves forward |
| `hasNextPage` | boolean | `nextCursor !== null` |
| `hasPreviousPage` | boolean | `previousCursor !== null` |

### 6.3 Basket admin scan page (`AdminBasketPageDto`)

Used by `GET /api/v1/basket/admin/carts` and `/abandoned`. The parameters are `cursor` and `pageSize` (default 20,
1–100). The response is `{ items, nextCursor, modifiedBefore }`.

A page can be **short, or even empty, while `nextCursor` is still set**, because each request scans a bounded number
of keys. Keep requesting until `nextCursor` is `null`. Details are in [basket.md](basket.md).

### 6.4 Dead-letter page (`OutboxDeadLetterPageDto`)

Used by `GET /api/v1/basket/admin/outbox/dead-letters/details`. The parameters are `offset` (default 0) and `limit`
(default 20, 1–100). The response is `{ total, offset, limit, items }`, and `total` is the length of the whole list.

### 6.5 Audit page (`GatewayAuditLogPageDto`)

Used by `GET /api/v1/admin/audit` on the gateway. The parameters are `cursor` and `pageSize` (default 50, 1–100). The
response is `{ items, nextCursor, unavailableServices }`. `nextCursor` is `null` once every service has been read to
its end, so it **stays non-null while a service is unavailable**: stop paging at an empty page (F-57). Details are in
[admin-platform.md](admin-platform.md#get-apiv1adminaudit).

### 6.6 Bare arrays

A few endpoints return a plain JSON array with no paging envelope, for example the category tree
(`GET /api/v1/categories`) and a user's sessions (`GET /api/v1/admin/users/{id}/sessions`). None of them takes paging
parameters. Each service file marks them.

```ts
export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface CursorPagedResult<T> {
  items: T[];
  pageSize: number;
  nextCursor: string | null;
  /** Always null today: forward-only. */
  previousCursor: string | null;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}
```

---

## 7. Rate limits

Every limiter is a **fixed window**.

| Where | Policy | Limit | Applies to |
|---|---|---|---|
| Gateway | global | **100 requests / 60 s** per client IP | Every request through the gateway |
| Each service | global | **100 requests / 60 s** per client IP | Every request to that service |
| Identity | `auth` | **10 / 60 s** | `register`, `refresh-token`, `revoke-token`, `confirm-email` |
| Identity | `login` | **5 / 60 s** | `login`, `forgot-password`, `reset-password` |
| Catalog | `search` | **30 / 60 s** | `GET /api/v1/products`, `GET /api/v1/products/newest` |
| Catalog | `bulk` | **10 / 60 s** | Bulk actions, import and export |

Separately from these limiters, Identity's login throttles an account after three failures and locks it for 10 minutes
after five. That answers **401 `Auth.TooManyAttempts`**, with the real time left in `detail`; see
[identity.md](identity.md#failed-logins-and-lockout).

Every 429, from the gateway or from a service, is the same: problem+json with `errorCode` `Request.RateLimited`, and a
`Retry-After` header giving the whole seconds left in the window (at least `1`). The gateway's global 429, captured
from the 101st request in one minute:

```json
{"title":"Too Many Requests","status":429,"detail":"Too many requests. Please retry later.",
 "errorCode":"Request.RateLimited","traceId":"00-5f08ff69db685519b2bd4b1b879af664-c41a7d50f8ce1dad-00"}
```

It came with `Retry-After: 60`. There is no `type` member on a 429.

"Per client IP" holds at both layers. The gateway passes the caller's address to the services in
`X-Forwarded-For`, and each service partitions on it. So one client using up its `login` allowance does not affect
another. This was verified after `0e1bc06`: after one client IP spent its `login` and `search` buckets, a second IP
got 401 and 200, not 429. Several browsers behind one NAT address still share a bucket.

---

## 8. Headers and CORS

### Request headers

| Header | When |
|---|---|
| `Authorization: Bearer <accessToken>` | Every call that needs a user |
| `Content-Type: application/json` | Every request with a JSON body |
| `X-Correlation-ID` | Optional; see below |

No endpoint accepts an idempotency key. Whether a write is safe to repeat is documented per endpoint.

### `X-Correlation-ID`

- **Echoed.** The gateway echoes the header on every response. If the request had none, the gateway generates one
  (for example `0HNOPIAT5M51B:00000001`).
- **Send your own to trace a request.** Send one per user action, for example a UUID without hyphens. The services
  then log it and record it on outbox messages and audit entries, so support can find the request.

> ⚠ Keep it at **100 characters or fewer.** A longer value makes any command that raises a domain event fail with
> **500** (observed: creating a product with a 101-character id fails; with 100 it succeeds). (F-16)
>
> ⚠ The id the gateway generates when you send none is **not forwarded** to the service, which then records a
> different id. The echoed value only identifies the request at the gateway. (F-22)

### CORS

- **Configuration.** The gateway allows the origins listed in `Cors:AllowedOrigins`, which defaults to
  `http://localhost:3000` and `https://localhost:3000`. It allows any method and any header, and allows credentials.
- **Other origins.** A preflight from an origin not on the list still answers 204, but without any
  `Access-Control-Allow-*` header, so the browser blocks the call.
- **Outside Development.** The gateway refuses to start if the list is empty.
- **Readable response headers.** Every actual response to an allowed origin (not the preflight, which does not need
  it) carries `Access-Control-Expose-Headers: Location,Retry-After,X-Correlation-ID,Content-Disposition`, errors and
  429s included, so browser JavaScript on the frontend's origin can read those four. Any other non-safelisted header
  stays hidden.

Even so, take a new resource's id from the response body rather than from `Location`: Identity's `Location` names the
internal service host (F-32), and every create returns the id in its body anyway.

---

## 9. Consistency and caching

**Cached reads.** These reads are cached in Redis:

| Read | Cached for |
|---|---|
| Product list, newest products, one product, a category's products | 5 min |
| Category tree | 10 min |
| One category | 5 min |
| One order | 5 min |
| A user's orders | 3 min |
| Own profile | 5 min |

**The service's own writes clear its cache.** So a client sees its own change on the next read; this was observed
for an order appearing in `/users/{userId}/orders` right after checkout. The cache lifetime matters only when a cache
entry was not evicted. Admins can clear Catalog's caches by hand ([catalog.md](catalog.md)).

**Some changes arrive asynchronously.** A few changes cross services by message, so they appear a little later than
the call that caused them:

| Action | What appears later | Observed delay (local stack) |
|---|---|---|
| Basket checkout (`POST /basket/{userId}/checkout`) | The order, in `GET /api/v1/users/{userId}/orders` | about 1 s |
| Order created | Its payment record, in `GET /api/v1/users/{userId}/payments` | 1 s to about 15 s after the order (observed 1 s, 8 s and 14 s) |
| Stripe confirms a payment | The order's status becomes Paid | about 14 s after Stripe's webhook ([payment.md](payment.md#how-a-customer-pays-the-card-flow)) |
| An admin refunds a payment | The order's status becomes Refunded | about 9 s after the refund |

Poll with a short back-off (for example 1 s, 2 s, 4 s, up to about 30 s) rather than assuming the result is there.
[flows.md](flows.md) gives the full sequences.

---

## 10. Query parameters

- **All query parameters are optional**, unless the endpoint says otherwise. When one is omitted, the documented
  default applies.
- **Unknown query parameters are ignored.** This is true in every service, including Catalog. A misspelled filter
  (`?search=` instead of `?searchTerm=`) quietly returns the unfiltered list.
- **Parameter names are camelCase** (`pageNumber`, `createdFrom`) and are matched case-insensitively.

### Dates: always send `Z`

Send dates as full ISO-8601 instants with `Z` or an offset: `2026-09-01T00:00:00Z` or `2026-09-01T00:00:00+03:00`.
Every service reads a date without a zone (`?from=2026-09-01`) as UTC, so it means midnight UTC at the **start** of
that day. `new Date(...).toISOString()` is the simplest way to say what you mean.

### Enum filters

Every enum query parameter, in every service, takes the **name in any case** (`Active`, `active`, `ACTIVE`): the
filters (`status`, Ordering's `statuses`), the sort columns (`sortBy`) and the stats buckets (`groupBy`). A number, or a
name that does not exist, is a 400 `ValidationError` keyed by the parameter (`errors.status`, `errors.sortBy`). Send
the PascalCase name, which is what the responses carry.

Where a filter may repeat (Ordering's `statuses`, Payment's and Notification's `status`), every value is checked.

---

## 11. CSV downloads

Two endpoints return CSV instead of JSON: `GET /api/v1/products/export` and `GET /api/v1/payments/export`. Both accept
the same filters as the matching list endpoint. Observed on both:

- `Content-Type: text/csv`.
- The body starts with a **UTF-8 byte-order mark** (`EF BB BF`), so Excel reads non-ASCII text correctly.
- `Content-Disposition: attachment; filename=<name>-<yyyyMMdd-HHmmss>.csv; filename*=UTF-8''<same>`.
- A header row with **PascalCase** column names, then one quoted row per item, with CRLF line endings.
- Enums are written as **names** (`Active`, `Success`), exactly as in JSON, money as `0.00`, and timestamps in round-trip UTC
  (`2026-09-16T10:55:12.1392410Z`).
- A text value that starts with `=`, `+`, `-`, `@`, tab or CR is prefixed with `'`. This stops spreadsheet formula
  injection, so do not strip the apostrophe when you parse the file.
- **At most 10 000 rows.** A filter that matches more is **refused with 400**, not truncated. The codes are
  `Products.ExportTooLarge` and `EXPORT_TOO_LARGE`. This is from source; the local data set is too small to reach the
  cap.

Downloading one: `fetch` with the bearer token, then `response.blob()`, then `URL.createObjectURL`, then a temporary
`<a download="…">` named from the `filename` in `Content-Disposition`, which the browser can read cross-origin
([§8](#8-headers-and-cors)).

---

## 12. Infrastructure endpoints

These exist on every component, but they are for operators and orchestration, not the frontend. Through the gateway,
only the **gateway's own** versions are reachable. The admin System page uses
[`GET /api/v1/admin/health`](admin-platform.md) instead.

| Path | Auth | Response |
|---|---|---|
| `GET /` | anonymous | An info object |
| `GET /health` | anonymous | `{ status, totalDurationMs, checks: [{ name, status }] }` over all checks. HTTP 200 when `Healthy` or `Degraded`, 503 when `Unhealthy` |
| `GET /health/ready` | anonymous | Same shape, readiness checks only |
| `GET /health/live` | anonymous | Same shape, liveness checks only |
| `GET /prometheus` | private networks only | Prometheus text format; **404** from a public address |
| `GET /metrics` | private networks only | OpenTelemetry metrics; **404** from a public address |
| `GET /openapi/v1.json` | anonymous | OpenAPI 3 document. Available in every environment except Production |
| `GET /scalar/v1` | anonymous | API reference UI, except in Production. **Not on Payment or the gateway** |

- **The gateway's own OpenAPI document** describes only the endpoints the gateway serves itself (`/` and the four
  admin endpoints), not the proxied APIs. Each service's document is reachable only inside the compose network.
- **What `GET /` returns.** Basket, Payment and Notification return only `service`, `version` and their health and
  metrics paths. The gateway, Identity, Catalog and Ordering also return `environment` (for example `"Sandbox"`) and a
  map of their routes. (F-17)

Captured from the gateway:

```json
{"service":"EShop API Gateway","version":"1.0.0","environment":"Sandbox","endpoints":{"health":"/health",
 "healthReady":"/health/ready","healthLive":"/health/live","metrics":{"prometheus":"/prometheus","otel":"/metrics"}}}
```

---

## Related documents

- [README](README.md): status, local ports, file map
- [flows.md](flows.md): end-to-end client flows
- [endpoint-index.md](endpoint-index.md): every endpoint on one page

---

**Version**: 1.0  
**Last Updated**: 2026-09-27
