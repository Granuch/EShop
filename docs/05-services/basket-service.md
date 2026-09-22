# Basket Service

Redis-backed shopping basket with an atomic checkout handoff to Ordering.

---

## Overview

Basket Service provides:
- One basket per user, stored in Redis
- Add, change, remove and clear, with every item priced from Catalog
- Checkout that re-checks every line against Catalog and hands the order to Ordering through messaging
- Price sync: a Catalog price change reprices the baskets that hold the product
- Owner-only changes; admins may read any basket
- Health, logs, traces and metrics

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | API host |
| Storage | Redis (StackExchange.Redis) | Baskets, reverse index, checkout outbox |
| Messaging | RabbitMQ + MassTransit (shared `AddEShopBus`) | Checkout events out, price changes in |
| Catalog | HTTP (`CatalogService:BaseUrl`) | Product names, prices and stock |
| Validation | FluentValidation + MediatR pipeline (Validation → Logging) | Request validation |
| Security | JWT + `BasketOwnerOrAdminRead` policy | Per-user access |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

There is no cache in front of Redis: `GET` reads the basket document directly.

---

## Project Structure

- `EShop.Basket.API` — endpoints, authorization, startup checks
- `EShop.Basket.Application` — commands, queries, checkout revalidation
- `EShop.Basket.Domain` — `ShoppingBasket`, `BasketItem`, limits
- `EShop.Basket.Infrastructure` — Redis repository, checkout store, outbox processor, price-sync consumer

---

## Redis Keys

| Key | Holds | Lifetime |
|-----|-------|----------|
| `basket:user:{userId}` | The basket document | 7 days, renewed on write |
| `basket:product:{productId}:users` | Users whose basket holds the product (price sync) | 7 days, renewed on write |
| `basket:product:{productId}:price-changed-at` | Time of the newest applied price change | 30 days |
| `basket:checkout:processing:{userId}` | Checkout in progress | 3 minutes |
| `basket:checkout:completed:{userId}` | Id of the user's last checkout | 24 hours |
| `basket:outbox:pending` / `processing` / `retry` / `dead` | Checkout events waiting to be published | Until published or replayed |
| `basket:corrupt:{userId}` | Unreadable documents a write replaced (10 kept) | 30 days |

---

## API

All routes are under `/api/v1/basket/{userId}` and need a JWT. The token's subject must equal `{userId}` exactly
(case-sensitive). An admin may `GET` any basket but change none but their own.

| Method and route | Success | Notes |
|------------------|---------|-------|
| `GET /{userId}` | 200 | A user with no basket gets an empty one (no items, zero totals, null dates) |
| `POST /{userId}/items` | 204 | Body `{ productId, quantity }`; name, price and stock come from Catalog |
| `PUT /{userId}/items/{productId}` | 204 | Body `{ quantity }`; 0 removes the line |
| `DELETE /{userId}/items/{productId}` | 204 | Also 204 when the product is not in the basket |
| `DELETE /{userId}` | 204 | Clears the basket |
| `POST /{userId}/checkout` | 200 `{ checkoutId }` | Body `{ shippingAddress: { street, city, state, zipCode, country } }` |

Limits: at most 999 of a product per line and 100 different products per basket.

Errors use the shared problem-details envelope with an `errorCode`:

| Status | Codes |
|--------|-------|
| 400 | `Validation.Failed`, `Basket.ValidationFailed`, `Basket.Empty` |
| 404 | `Basket.NotFound`, `Basket.ItemNotFound`, `Basket.ProductNotFound` |
| 409 | `Basket.ConcurrentUpdate`, `Basket.CheckoutConflict`, `Basket.CheckoutInProgress`, `Basket.InsufficientStock`, `Basket.CheckoutRevalidationFailed` |
| 503 | `Basket.OperationFailed`, `Basket.PersistenceFailed`, `Basket.ProductVerificationFailed` (Redis or Catalog unavailable) |

`Basket.ConcurrentUpdate` means the basket kept changing while a write was retried (5 attempts); retry the request.

---

## Checkout

1. Every line is re-read from Catalog. A product that is no longer available, short of stock, or repriced refuses the
   checkout with 409 `Basket.CheckoutRevalidationFailed` and a `lines` member giving each line's reason. The basket takes
   Catalog's current prices, so the next checkout is at a price the customer has seen.
2. One Redis transaction then queues the checkout event, deletes the basket and its index entries, and records the
   completed checkout. It applies only if the basket is unchanged since it was read (409 `Basket.CheckoutConflict`
   otherwise), so a checkout either happened completely or did nothing.
3. A request that finds no basket while a completed checkout is recorded is a repeat, and returns that checkout's id.

`checkoutId` is the event's id, which is also the MassTransit `MessageId` Ordering deduplicates on.

The `BasketCheckedOutEvent` carries the user, the lines with Basket's prices, the total, the structured shipping
address and `Currency` (`USD`). Ordering refuses a checkout in any other currency. There is no payment method: Payment
chooses it when the payment intent is created.

### Outbox

A background processor publishes queued events. A failed publish is retried after 5 s, 15 s, 1 min, 5 min, 15 min,
30 min and then hourly: 10 attempts, about 4.5 hours. After that the event is dead-lettered and kept until an admin
replays it:
- `GET /api/v1/basket/admin/outbox/dead-letters` returns the count.
- `GET /api/v1/basket/admin/outbox/dead-letters/details` lists them (see [Admin reads](#admin-reads)).
- `POST /api/v1/basket/admin/outbox/dead-letters/replay` requeues them (Admin only).

Outbox health reports `Degraded` while any dead letter exists.

A message dead-lettered since the admin panel's S14 records why and when: `failureReason` (`PublishFailed`,
`PublishTimedOut` or `Unpublishable`), the last exception's type name (never its message, which can name hosts and
users) and `deadLetteredAtUtc`. These fields are written only on the way into the dead-letter list, so an envelope in
pending, processing or retry is unchanged. Messages dead-lettered earlier have none of them.

---

## Admin reads

Read-only, by decision: an admin can see carts and change none. All three need a JWT; the gateway also requires the
`Admin` role for everything under `/api/v1/basket/admin/`.

| Method and route | Permission | Returns |
|------------------|------------|---------|
| `GET /api/v1/basket/admin/carts?cursor=&pageSize=` | `baskets.read` | Every stored basket, summarised |
| `GET /api/v1/basket/admin/abandoned?olderThan=24h&cursor=&pageSize=` | `baskets.read` | Baskets unchanged for longer than `olderThan` |
| `GET /api/v1/basket/admin/outbox/dead-letters/details?offset=&limit=` | `system.manage` | Dead letters, newest first |

**Carts and abandoned carts** walk Redis with `SCAN … MATCH basket:user:*`, never `KEYS`, which would block the server
for the whole keyspace.
- A page holds at most `pageSize` baskets (default 20, at most 100).
- One request makes at most 20 `SCAN` calls, about 5,000 keys. A page can therefore be short, or empty, while
  `nextCursor` is still set. Keep passing `nextCursor` back until it is `null`.
- The order is Redis's, not a sort.
- A basket stored for the whole walk is listed at least once. It can occasionally appear twice (the `SCAN` contract),
  so de-duplicate by `userId` if you collect a whole walk.
- Each summary gives `userId`, `isReadable`, `lines`, `totalItems`, `totalPrice`, `currency`, `createdAt` and
  `lastModifiedAt`. The lines themselves come from `GET /api/v1/basket/{userId}`.
- An unreadable basket document is listed with `isReadable: false` and no figures.

`olderThan` is a whole number and a unit: `90m`, `24h` or `3d`, up to 30 days. A basket is abandoned when its
`lastModifiedAt` is strictly before now minus `olderThan`. Every change moves `lastModifiedAt`, including a price sync.
An unreadable basket is never listed as abandoned. The response echoes the cutoff as `modifiedBefore`.

**Dead-letter details** give, for each entry, `messageId` (the customer's `checkoutId`), `eventType`, `occurredOnUtc`,
`deadLetteredAtUtc`, `attempts`, `error`, `exceptionType` and `correlationId`, plus `total` for the whole list.
- The order itself, with its shipping address, is never returned.
- An entry the processor could not parse is listed with `isReadable: false`.
- Paging is by offset (default 20, at most 100). A replay or a new dead letter between two pages shifts the list.

---

## Concurrency

Every basket write is conditioned on the stored document it read. If another write landed first, the change is redone
on a fresh read, up to 5 times. Two tabs, a price sync and a checkout therefore cannot overwrite each other.

---

## Price Sync

`ProductPriceChangedConsumer` handles Catalog's price-change events. It reprices each basket that holds the product,
16 at a time, and drops users whose basket no longer holds it from the reverse index. Only the newest change per product
is applied: an older event that arrives late changes nothing.

---

## Configuration and Startup Checks

| Setting | Rule |
|---------|------|
| `ConnectionStrings:Redis` | Required in every environment |
| `CatalogService:BaseUrl` | Required, absolute URI (no tracked default) |
| `JwtSettings:SecretKey` | At least 32 characters; no placeholder outside Development and Testing (`JwtSecretGuard`) |
| Redis and `RabbitMQ:Host`/`Username`/`Password` | No placeholder (`#{…}#`, `CHANGE_ME`, …) outside Development and Testing |
| `Cors:AllowedOrigins` | No placeholder origin outside Development and Testing (`CorsOriginGuard`) |

A failed check stops the host at startup. Sandbox is checked like Production.

---

## Health and Telemetry

- `/health/ready`: Redis (the application's own connection), outbox, readiness and RabbitMQ when configured
- `/health/live`: liveness
- `/prometheus` and `/metrics`: restricted to private networks unless `Metrics:AllowedNetworks` says otherwise
- `GET /`: service name, version and the paths above, with no environment name and no route map

The shipping address is redacted from request logs.

---

## Related Documents

- [Catalog Service](catalog-service.md)
- [Ordering Service](ordering-service.md)
- [Infrastructure - Caching](../06-infrastructure/caching.md)
- [Infrastructure - Message Broker](../06-infrastructure/message-broker.md)

---

**Version**: 3.1  
**Last Updated**: 2026-09-22
