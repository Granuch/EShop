# Ordering Service

Order lifecycle service that coordinates with basket, payment, and notification workflows.

---

## Overview

Ordering Service provides:
- Order creation and lifecycle management
- User and administrative order query paths
- Event-driven coordination with upstream/downstream services
- Domain/application separation for business rules
- Health and telemetry integration

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | API host |
| Database | PostgreSQL | Order persistence |
| Cache/auxiliary | Redis (configured scenarios) | Distributed cache support |
| Messaging | RabbitMQ + MassTransit | Async workflow coordination |
| Validation | FluentValidation + MediatR pipeline | Command/query validation |
| Security | JWT + policy handlers | User/admin authorization |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

---

## Project Structure

Ordering service follows layered architecture:

- `EShop.Ordering.API`
- `EShop.Ordering.Application`
- `EShop.Ordering.Domain`
- `EShop.Ordering.Infrastructure`

---

## Runtime Characteristics

### Startup Guards

Ordering validates critical config (JWT and connection settings) and enforces stricter checks in non-local environments.

### Messaging Integration

Consumes and publishes workflow events through MassTransit/RabbitMQ.

### Authorization

Uses role/policy-based authorization, including user-scoped access policies.

---

## API Areas (High Level)

**Full endpoint-by-endpoint contracts (routes, both auth layers, request/response shapes, error
tables, TypeScript types, the `OrderStatus` state machine) live in
[frontend/ordering.md](../01-overview/frontend/ordering.md), the authoritative source. This
section is a summary, not a duplicate.**

17 endpoints:
- **Storefront** (owner or admin; `userId` is forced to the caller for non-admins on create):
  create an order, get by id, `GET /api/v1/users/{userId}/orders` (filtered/paged), item
  add/update/remove, shipping-address update, cancel. Item and address changes are refused
  (409) once the order is paid.
  - An order is normally created asynchronously from a basket checkout, so the client polls
    `GET /api/v1/users/{userId}/orders` for it rather than getting one back from checkout.
  - `OrderStatus` is sent as an **integer** everywhere except the admin list's `status` filter,
    which takes the name.
- **Admin** (gateway `Authenticated` only — see the note below — service's own `Admin` check):
  list (name-only status filter), stats (buckets and window totals), notes (add/list), status
  history (with the actor), ship, deliver.

⚠ **Ordering's whole admin surface relies only on the service's own `Admin` check — the gateway
has no dedicated role-gated route for it**, unlike Identity/Basket/Notification/most of Catalog
(confirmed for all 6 admin endpoints, not a bypass — see
[frontend/ordering.md](../01-overview/frontend/ordering.md#frontend-notes)).

Exact route exposure is mediated by gateway policy and service authorization rules.

Admin-reachable commands are recorded in this service's `audit_log` and served on `GET /api/v1/admin/audit`
(`audit.read`); see [Admin Audit Trail](../03-architecture/audit-log.md).

`GET /api/v1/admin/settings` (`system.manage`, admin panel S19) is this service's slice of the System page's read-only
settings: the pricing currency (`Order.PricingCurrency`, USD) and `taxApplied`/`shippingCharged`, both `false` because an
order's total is the sum of its lines. The gateway serves the composed page on the same path and does not route it here.

---

## Workflow Role

Ordering is the central service for order state progression:
- receives checkout-triggered flow input
- persists order state
- reacts to payment outcomes
- emits order events for dependent services (for example notifications)

---

## Health and Telemetry

Ordering service exposes health endpoints and emits:
- Structured logs
- OpenTelemetry traces/metrics
- Prometheus metrics endpoint support

---

## Operational Notes

- Keep order transition rules explicit and tested.
- Track eventual consistency across payment/notification integrations.
- Use traces to diagnose end-to-end order flow latency.

---

## Related Documents

- [Frontend contracts: Ordering](../01-overview/frontend/ordering.md) — the authoritative endpoint reference
- [Basket Service](basket-service.md)
- [Payment Service](payment-service.md)
- [Notification Service](notification-service.md)
- [Infrastructure - Databases](../06-infrastructure/databases.md)

---

**Version**: 2.1  
**Last Updated**: 2026-09-26
