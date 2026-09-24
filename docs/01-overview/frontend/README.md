# Frontend API contracts

The contract a client application (the `ui/` Next.js app, or any other) builds against. It covers every HTTP endpoint
of the EShop platform, for the storefront and the admin panel, with field tables and copy-ready TypeScript types.

---

## Status

- **Authoritative, and kept in sync with the code.**
  - Every endpoint is checked against three things: the C# source, the service's live `/openapi/v1.json`, and real
    responses captured through the gateway on the local compose stack.
  - Casing, `null` rendering, enum forms and error bodies are copied from captured responses, never inferred.
- **The code still wins.** If a response differs from these docs, the docs are wrong. Report it and fix the docs.
- **Quirks are recorded, not fixed.** Where the API behaves awkwardly, the docs describe what it actually does and mark
  it with ⚠. Each ⚠ carries an id such as `(F-04)`, which identifies the underlying code issue.
- **Keeping it in sync.** Any change to an endpoint, request or DTO must update the matching file here in the same
  change.
- **Verified at:** `105d647` on `feature/admin-panel` (2026-09-23), unless a file states its own commit.

This set replaces [Data Contracts.md](../Data%20Contracts.md), which predates most of the admin panel and had drifted
from the code.

### Progress

The set is being written service by service. Until a file is marked **complete**, use the service's OpenAPI document
together with [conventions.md](conventions.md).

| File | State |
|---|---|
| [conventions.md](conventions.md) | complete |
| [identity.md](identity.md) | complete |
| [catalog.md](catalog.md) | complete |
| [basket.md](basket.md) | outline only |
| [ordering.md](ordering.md) | outline only |
| [payment.md](payment.md) | outline only |
| [notification.md](notification.md) | outline only |
| [admin-platform.md](admin-platform.md) | outline only |
| [flows.md](flows.md) | outline only |
| [endpoint-index.md](endpoint-index.md) | outline only |

---

## How to read these files

Each service file has the same layout:

1. **Header.** The public base paths and the gateway policy on each.
2. **Storefront / customer.** The endpoints a shopper's session calls.
3. **Admin panel.** The endpoints the back office calls.
4. **Types.** A field table for every DTO, then a TypeScript block.
5. **Frontend notes.** The ⚠ quirks, each with its finding id.

Every endpoint states its auth at **both layers**: the gateway's route policy and the service's own policy or
permission. For example, "gateway: `Admin` role · service: `notifications.read`". A request must pass both. See
[conventions.md §5](conventions.md#5-permissions-and-admin-access).

**Names are wire names.** Tables and TypeScript use the camelCase names actually sent (`stockQuantity`). The C# type
is named once, as a source reference (`ProductDto`).

**Enums are written as they are sent**, because the services differ
([conventions.md §2](conventions.md#enums)):
- numeric enums become a `const` object plus a type:
  ```ts
  export const OrderStatus = { Pending: 0, Paid: 1 /* … */ } as const;
  export type OrderStatus = (typeof OrderStatus)[keyof typeof OrderStatus];
  ```
- string enums become a string-literal union: `type PaymentStatus = 'PENDING' | 'SUCCESS' | …`.

**Nullable means present with the value `null`.** Responses write nulls out, so a nullable field is typed `T | null`.
An optional (`?`) property appears only in request types.

---

## Base URL and local setup

Clients call **only the API gateway**. Paths are not rewritten, so the path you call is the service's own path.

| Environment | Base URL |
|---|---|
| Local compose stack | `http://localhost:7000` |

Start the local stack with:

```bash
docker compose --profile sandbox up -d --build
```

Always pass `--build`: a plain `up` reuses stale images. Add `--profile stripe` for the Stripe webhook listener,
which the payment flow needs.

### Local ports

| Port | What | Use from the frontend |
|---|---|---|
| **7000** | API gateway | **The only API base URL** |
| 7001, 7004, 7005, 7006, 7008 | Identity, Catalog, Ordering, Basket, Payment, bound to `127.0.0.1` | Debugging only. They skip the gateway's route policies, body caps and rate limit; each service still checks its own auth |
| — | Notification | Not published; reachable only through the gateway |
| 8025 | Mailpit (captured outgoing email) | Read password-reset and order emails locally |
| 3000 | Grafana | See the ⚠ below |
| 5341 | Seq (logs) | Search logs by `X-Correlation-ID` |
| 16686 | Jaeger (traces) | |

> ⚠ **Port 3000 is also Grafana's.** Next.js `dev` defaults to 3000, and the gateway's default CORS origins are
> `http://localhost:3000` and `https://localhost:3000`. Grafana is published on `127.0.0.1:3000`, so what happens
> depends on the OS:
> - **On Windows** (observed), both can listen at once. `http://localhost:3000` reached the Node server over IPv6
>   (`::1`), while `http://127.0.0.1:3000` reached Grafana. Open the app as `localhost`.
> - **Elsewhere**, `next dev` may find the port taken and move to 3001. That origin is not allowed, so the browser
>   blocks every API call on CORS.
>
> The simplest fix is to move Grafana: set `GRAFANA_PORT` in `.env`. Alternatively, add your dev origin to
> `Cors:AllowedOrigins` (`CORS_ORIGIN_1`/`CORS_ORIGIN_2` in `.env`). (F-06)

### Test accounts

- **Admin.** The seeded admin's email and password come from `IDENTITY_SEED_ADMIN_EMAIL` and
  `IDENTITY_SEED_ADMIN_PASSWORD` in `.env`.
- **Customers.** Register one with `POST /api/v1/auth/register`. It can log in immediately (see
  [conventions.md §4](conventions.md#4-authentication)).
- **Catalog content.** The seeded products and categories are placeholder content. They do not define the store.

---

## File map

| File | Covers |
|---|---|
| [conventions.md](conventions.md) | Rules shared by every endpoint: routing, JSON, errors, auth, permissions, paging, rate limits, headers, CORS, caching, query parameters, CSV, infrastructure routes |
| [identity.md](identity.md) | Registration, login, tokens, 2FA, profile; admin users and roles |
| [catalog.md](catalog.md) | Products and categories, images, attributes, discounts; admin catalog, bulk actions, import/export, cache |
| [basket.md](basket.md) | The shopper's basket and checkout; admin basket views and outbox |
| [ordering.md](ordering.md) | Orders, items, shipping address, cancel; admin order list, stats, notes, history, ship, deliver |
| [payment.md](payment.md) | Payment intents and history; admin payments, export, settle, refund, events |
| [notification.md](notification.md) | Admin notification journal, templates, retry, resend |
| [admin-platform.md](admin-platform.md) | Endpoints the gateway serves itself: merged audit log, system health, settings, feature flags |
| [flows.md](flows.md) | End-to-end flows: sign-up and login, checkout to paid order, cancel and refund, admin tasks |
| [endpoint-index.md](endpoint-index.md) | Every endpoint on one page, with its auth and a link |

---

## Related documents

- [Architecture diagram](../architecture-diagram.md)
- [Service documentation](../../05-services/)

---

**Version**: 1.0  
**Last Updated**: 2026-09-24
