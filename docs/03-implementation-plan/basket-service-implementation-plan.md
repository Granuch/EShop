# Basket Service Implementation Plan (EShop)

## 0. Scope and Architectural Baseline

This plan defines a full implementation of `Basket` as a Redis-first microservice that follows existing EShop patterns from Catalog/Ordering/Identity:

- DDD layers: `Domain -> Application -> Infrastructure -> API`
- CQRS with MediatR
- `Result` / `Result<T>` in Application handlers
- FluentValidation per command/query
- Cache + cache invalidation behaviors
- JWT validation + policy authorization
- Serilog structured logging
- OpenTelemetry + Prometheus
- Health checks (`/health`, `/health/ready`, `/health/live`)
- Rate limiting
- Global exception handler middleware
- Forwarded headers support
- Multi-stage Dockerfile
- Environment-specific settings (`appsettings.*.json`)

For background/event processing, use `.NET 10` `BackgroundService` (consistent with workspace conventions).

---

## 1. Target Project Structure

```plaintext
src/Services/Basket/
├─ EShop.Basket.Domain/
│  ├─ Entities/
│  │  ├─ ShoppingBasket.cs
│  │  └─ BasketItem.cs
│  ├─ Events/
│  │  └─ BasketCheckedOutDomainEvent.cs
│  ├─ Interfaces/
│  │  └─ IBasketRepository.cs
│  └─ EShop.Basket.Domain.csproj
│
├─ EShop.Basket.Application/
│  ├─ Extensions/
│  │  └─ ServiceCollectionExtensions.cs
│  ├─ Telemetry/
│  │  └─ BasketActivitySource.cs
│  ├─ Commands/
│  │  ├─ AddItemToBasket/
│  │  │  ├─ AddItemToBasketCommand.cs
│  │  │  ├─ AddItemToBasketCommandHandler.cs
│  │  │  └─ AddItemToBasketCommandValidator.cs
│  │  ├─ UpdateBasketItemQuantity/
│  │  │  ├─ UpdateBasketItemQuantityCommand.cs
│  │  │  ├─ UpdateBasketItemQuantityCommandHandler.cs
│  │  │  └─ UpdateBasketItemQuantityCommandValidator.cs
│  │  ├─ RemoveBasketItem/
│  │  │  ├─ RemoveBasketItemCommand.cs
│  │  │  ├─ RemoveBasketItemCommandHandler.cs
│  │  │  └─ RemoveBasketItemCommandValidator.cs
│  │  ├─ ClearBasket/
│  │  │  ├─ ClearBasketCommand.cs
│  │  │  ├─ ClearBasketCommandHandler.cs
│  │  │  └─ ClearBasketCommandValidator.cs
│  │  └─ CheckoutBasket/
│  │     ├─ CheckoutBasketCommand.cs
│  │     ├─ CheckoutBasketCommandHandler.cs
│  │     └─ CheckoutBasketCommandValidator.cs
│  ├─ Queries/
│  │  └─ GetBasket/
│  │     ├─ GetBasketQuery.cs
│  │     ├─ GetBasketQueryHandler.cs
│  │     ├─ GetBasketQueryValidator.cs
│  │     └─ BasketDto.cs
│  └─ EShop.Basket.Application.csproj
│
├─ EShop.Basket.Infrastructure/
│  ├─ Extensions/
│  │  └─ ServiceCollectionExtensions.cs
│  ├─ Configuration/
│  │  └─ RedisBasketOptions.cs
│  ├─ Repositories/
│  │  └─ RedisBasketRepository.cs
│  ├─ Consumers/
│  │  └─ ProductPriceChangedConsumer.cs
│  ├─ Outbox/
│  │  ├─ BasketRedisOutbox.cs
│  │  └─ BasketRedisOutboxProcessorService.cs
│  ├─ Idempotency/
│  │  └─ RedisMessageIdempotencyStore.cs
│  ├─ Caching/
│  │  ├─ CircuitBreakingDistributedCache.cs
│  │  └─ CacheCircuitBreakerExtensions.cs
│  └─ EShop.Basket.Infrastructure.csproj
│
└─ EShop.Basket.API/
   ├─ Endpoints/
   │  └─ BasketEndpoints.cs
   ├─ Infrastructure/
   │  ├─ Configuration/
   │  │  └─ JwtSettings.cs
   │  ├─ HealthChecks/
   │  │  └─ BasketHealthChecks.cs
   │  ├─ Middleware/
   │  │  └─ GlobalExceptionHandlerMiddleware.cs
   │  └─ Security/
   │     ├─ ClaimsPrincipalExtensions.cs
   │     └─ SameUserOrAdminAuthorization.cs
   ├─ Program.cs
   ├─ Dockerfile
   ├─ appsettings.json
   ├─ appsettings.Development.json
   ├─ appsettings.Production.json
   └─ appsettings.Testing.json
```

---

## 2. Domain Layer Plan

## `ShoppingBasket` (`EShop.Basket.Domain.Entities`)
- Inherits: `AggregateRoot<string>`
- Properties:
  - `Id` (userId)
  - `UserId`
  - `IReadOnlyCollection<BasketItem> Items`
  - `DateTime LastModifiedAt`
  - `decimal TotalPrice` (computed)
  - `int TotalItems` (computed)
- Methods:
  - `Create(string userId)`
  - `AddItem(Guid productId, string productName, decimal price, int quantity)`
  - `UpdateItemQuantity(Guid productId, int quantity)`
  - `RemoveItem(Guid productId)`
  - `Clear()`
  - `ApplyPriceChange(Guid productId, decimal newPrice)`
  - `Checkout(string shippingAddress, string paymentMethod)` -> raises domain event
- Domain event produced:
  - `BasketCheckedOutDomainEvent`

## `BasketItem` (`EShop.Basket.Domain.Entities`)
- Inherits: `Entity<Guid>`
- Properties:
  - `ProductId`, `ProductName`, `Price`, `Quantity`, `SubTotal`
- Methods:
  - constructor validation
  - `UpdateQuantity(int newQuantity)`
  - `UpdatePrice(decimal newPrice)`

## `BasketCheckedOutDomainEvent` (`EShop.Basket.Domain.Events`)
- Implements: `IDomainEvent`
- Payload:
  - `UserId`
  - item snapshot (productId, name, price, qty)
  - `TotalPrice`
  - `ShippingAddress`
  - `PaymentMethod`
- Purpose: internal domain event for conversion to `BasketCheckedOutEvent` integration event

## `IBasketRepository` (`EShop.Basket.Domain.Interfaces`)
- `GetBasketAsync(userId)`
- `SaveBasketAsync(basket)`
- `DeleteBasketAsync(userId)`
- `GetUsersContainingProductAsync(productId)` (for price sync fan-out)

---

## 3. Application Layer Plan (CQRS + Validation + Result)

## Commands

### AddItemToBasketCommand
- Interfaces: `IRequest<Result<Unit>>`, `ICacheInvalidatingCommand`
- Properties: `UserId, ProductId, ProductName, Price, Quantity`
- Cache invalidation: `basket:user:{UserId}`
- Handler flow:
  1. Load basket or create new
  2. Add/update item
  3. Save to Redis via repository
  4. Return success
- Validator:
  - `UserId` required
  - `ProductId` not empty
  - `ProductName` required
  - `Price >= 0`
  - `Quantity > 0`

### UpdateBasketItemQuantityCommand
- Interfaces: `IRequest<Result<Unit>>`, `ICacheInvalidatingCommand`
- Properties: `UserId, ProductId, Quantity`
- Invalidate: `basket:user:{UserId}`
- Handler:
  - basket exists? else not found failure
  - apply quantity change
  - if empty -> delete basket, else save
- Validator: `Quantity >= 0`

### RemoveBasketItemCommand
- Interfaces: `IRequest<Result<Unit>>`, `ICacheInvalidatingCommand`
- Properties: `UserId, ProductId`
- Invalidate: `basket:user:{UserId}`
- Handler:
  - load basket
  - remove product
  - save/delete
- Validator: required IDs

### ClearBasketCommand
- Interfaces: `IRequest<Result<Unit>>`, `ICacheInvalidatingCommand`
- Properties: `UserId`
- Invalidate: `basket:user:{UserId}`
- Handler: delete basket key, return success even if already absent (idempotent)

### CheckoutBasketCommand
- Interfaces: `IRequest<Result<Guid>>`, `ICacheInvalidatingCommand`
- Properties: `UserId, ShippingAddress, PaymentMethod`
- Invalidate: `basket:user:{UserId}`
- Handler:
  1. Load basket
  2. Fail if empty
  3. Call `basket.Checkout(...)`
  4. Create/persist integration event via outbox abstraction
  5. Delete basket
  6. Return correlation/order tracking GUID
- Validator: all fields required

## Query

### GetBasketQuery
- Interfaces: `IRequest<Result<BasketDto?>>`, `ICacheableQuery`
- Properties: `UserId`
- Cache key: `basket:user:{UserId}`
- Cache duration: short (1-3 min)
- Handler:
  - read from repo
  - map to dto
  - return success with null if absent (or NotFound error; pick one contract and keep consistent)
- Validator: `UserId` required

---

## 4. Infrastructure Layer Plan

## RedisBasketRepository
- Key patterns:
  - Basket: `basket:user:{userId}`
  - Reverse index per product: `basket:product:{productId}:users`
- Storage format: JSON serialized `ShoppingBasket`
- TTL policy:
  - default basket TTL: 7 days
  - refresh TTL on every write
- Methods:
  - `GetBasketAsync`: get + deserialize
  - `SaveBasketAsync`: set JSON with expiry + sync product-user index
  - `DeleteBasketAsync`: delete basket + cleanup index entries

## Outbox strategy for Redis-based service
Because no EF/Postgres unit-of-work exists, implement lightweight Redis outbox:

1. `BasketRedisOutbox`:
   - append serialized `BasketCheckedOutEvent` to Redis stream/list
2. `BasketRedisOutboxProcessorService : BackgroundService`:
   - poll pending outbox entries
   - publish via MassTransit
   - mark processed
   - retry with max attempts
   - dead-letter failed messages
3. Guarantees:
   - at-least-once publish
   - no event loss on transient broker outage

## ProductPriceChanged consumer
- Consumer: `ProductPriceChangedConsumer`
- Input event: `ProductPriceChangedIntegrationEvent`
- Idempotency:
  - use `RedisMessageIdempotencyStore` (`SETNX` with TTL) keyed by message ID
- Flow:
  1. reject duplicates
  2. fetch affected userIds from product reverse index
  3. load each basket, update matching item price
  4. save basket with renewed TTL
  5. record metrics

## Service registrations (`AddBasketInfrastructure`)
- Redis connection + distributed cache
- Circuit-breaking distributed cache wrapper
- Repository + options binding
- Idempotency store
- Outbox + outbox processor hosted service
- MassTransit consumer registration

---

## 5. API Layer Plan

## Program.cs configuration checklist
- Serilog bootstrap + configured sinks
- Forwarded headers config (known proxies)
- Add Basket application/infrastructure services
- JWT bearer auth (validation only)
- Authorization policies (`SameUserOrAdmin`)
- CORS (`AllowFrontend`)
- Rate limiter
- OpenAPI/Scalar
- Health checks
- Prometheus (`/prometheus`) + OTel (`/metrics`)
- Middleware order:
  1. Global exception handler
  2. Forwarded headers
  3. Serilog request logging
  4. CORS
  5. Rate limiter
  6. HTTPS redirection
  7. HTTP metrics
  8. Authentication
  9. Authorization
  10. Endpoint mapping

## Endpoints (`/api/v1/basket`)
- `GET /{userId}` -> get basket
- `POST /{userId}/items` -> add item
- `PUT /{userId}/items/{productId}` -> update qty
- `DELETE /{userId}/items/{productId}` -> remove item
- `DELETE /{userId}` -> clear basket
- `POST /{userId}/checkout` -> checkout
- All protected with owner-or-admin policy

## Middleware/Health/Security
- Global exception middleware aligned with Catalog/Ordering format (`application/problem+json`)
- Readiness/liveness health checks:
  - Redis connectivity
  - outbox processor health
- Claims helper + authorization handler reused pattern from Ordering

---

## 6. Integration Contract Plan

## Basket publishes
- `BasketCheckedOutEvent`
  - userId
  - checkout items
  - total
  - shipping info
  - payment method
  - correlation/version metadata

## Basket consumes
- `ProductPriceChangedIntegrationEvent`
  - synchronize unit prices for matching product in open baskets

## Ordering interaction
- No Ordering contract changes required
- Existing `BasketCheckedOutConsumer` in Ordering continues creating order from event payload

---

## 7. Observability and Platform Config Plan

## Prometheus (`infrastructure/prometheus/prometheus.yml`)
Add:
- `basket-api` scrape (`/prometheus`)
- `basket-api-otel` scrape (`/metrics`)

## Recording rules (`recording-rules.yml`)
Add `spm:basket_business`:
- checkout rate (success/failure)
- checkout failure ratio
- items added rate
- price sync updates rate
- checkout amount p95 histogram quantile

## Grafana (`basket-dashboard.json`)
Panels:
- service up/down
- request rate
- 5xx error ratio
- latency p50/p95/p99
- items added/sec
- checkout success/failure
- checkout amount distribution
- price sync throughput
- Redis latency/errors

## OTel Collector
- likely no structural change required if Basket exports to existing OTLP endpoint

---

## 8. Testing Plan

## Unit tests
- Domain:
  - basket invariants
  - add/remove/update/clear behavior
  - checkout raises event
  - price change update
- Application:
  - handlers success/failure
  - validators
  - cache invalidation keys

## Integration tests
- API tests with `Testing` environment
- Use Redis test container (preferred) or isolated local redis DB index
- Cases:
  - full basket lifecycle
  - checkout generates outbox + published integration event
  - duplicate product price event is ignored (idempotency)
  - authorization policy enforcement
  - health endpoints

---

## 9. Architectural Decisions and Trade-offs

1. **Redis as primary store**
   - Pros: fast, natural TTL support
   - Cons: no relational transactions

2. **Outbox without EF**
   - Decision: Redis-backed outbox + `BackgroundService`
   - Trade-off: custom reliability logic, but preserves event durability

3. **Idempotency without `IdempotentConsumer<T, TDbContext>`**
   - Decision: Redis key-based dedup
   - Trade-off: time-windowed dedup via TTL

4. **Price synchronization strategy**
   - Decision: eager update on `ProductPriceChangedIntegrationEvent`
   - Trade-off: write amplification vs accurate checkout totals

5. **Concurrency**
   - Decision: optimistic last-write-wins initially; optional ETag/version CAS enhancement later

---

## 10. Implementation Order (Execution Sequence)

1. Finalize contracts and key naming conventions
2. Complete Domain entities/events and invariants
3. Implement Redis repository with TTL + reverse index
4. Implement Application commands/queries/validators
5. Add Redis outbox + outbox processor `BackgroundService`
6. Add product price change consumer + idempotency store
7. Wire Infrastructure registrations and MassTransit
8. Build API Program.cs pipeline and endpoints
9. Add security middleware/policies/health checks
10. Add observability (metrics, OTel, dashboards, rules)
11. Add Dockerfile and appsettings hierarchy
12. Implement unit and integration tests
13. Run full build and test suite, then harden edge cases
