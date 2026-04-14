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

Typical capabilities include:
- User order list/detail
- Order lifecycle actions (where allowed)
- Admin-focused order visibility/management paths

Exact route exposure is mediated by gateway policy and service authorization rules.

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

- [Basket Service](basket-service.md)
- [Payment Service](payment-service.md)
- [Notification Service](notification-service.md)
- [Infrastructure - Databases](../06-infrastructure/databases.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
