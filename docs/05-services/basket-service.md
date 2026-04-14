# Basket Service

Basket state service backed by Redis, with checkout handoff into asynchronous order flow.

---

## Overview

Basket Service provides:
- Basket retrieval and update operations
- Item add/update/remove semantics
- Redis-backed basket persistence
- Checkout initiation path into downstream workflow
- Auth-aware access controls
- Health and telemetry integration

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | API host |
| Storage | Redis | Basket state persistence |
| Messaging | RabbitMQ + MassTransit | Checkout integration events |
| Validation | FluentValidation + application pipeline | Request validation |
| Security | JWT + policy handlers | User/admin access control |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

---

## Project Structure

Basket service follows layered architecture:

- `EShop.Basket.API`
- `EShop.Basket.Application`
- `EShop.Basket.Domain`
- `EShop.Basket.Infrastructure`

---

## Runtime Characteristics

### Redis Dependency

Basket service requires Redis connectivity for normal runtime behavior.

### Authorization

Includes policy-based checks (for example same-user-or-admin patterns) and JWT authentication.

### Resilience

Circuit-breaking cache wrapper and operational guards are used for dependency instability scenarios.

---

## API Areas (High Level)

Common basket capabilities:
- Get basket by user
- Add/update/remove basket items
- Checkout basket
- Clear/delete basket

Route protection is enforced via auth policies for user-scoped operations.

---

## Checkout Integration

Checkout is designed to hand off processing to downstream services through messaging rather than blocking on full order/payment completion in a single synchronous request.

---

## Health and Telemetry

Basket service exposes health endpoints and emits:
- Structured logs
- OpenTelemetry traces/metrics
- Prometheus metrics endpoint support

---

## Operational Notes

- Keep Redis configuration reliable across environments.
- Validate basket authorization paths for user isolation.
- Monitor checkout handoff latency and failure signals through traces and logs.

---

## Related Documents

- [Catalog Service](catalog-service.md)
- [Ordering Service](ordering-service.md)
- [Infrastructure - Caching](../06-infrastructure/caching.md)
- [Infrastructure - Message Broker](../06-infrastructure/message-broker.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
