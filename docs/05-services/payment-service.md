# Payment Service

Payment processing service with Stripe-oriented integration and event-driven workflow participation.

---

## Overview

Payment Service provides:
- Payment lifecycle handling
- Stripe integration paths and webhook-related runtime support
- Event-driven communication with ordering workflow
- Configuration and safety guards by environment
- Health and telemetry integration

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | API host |
| Database | PostgreSQL | Payment persistence |
| Messaging | RabbitMQ + MassTransit | Async workflow events |
| Payment integration | Stripe configuration/services | Payment processing |
| Security | JWT + policy handlers | Protected payment access |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

---

## Project Structure

Payment service follows layered architecture:

- `EShop.Payment.API`
- `EShop.Payment.Application`
- `EShop.Payment.Domain`
- `EShop.Payment.Infrastructure`

---

## Runtime Characteristics

### Environment Safety

Startup includes strict checks for:
- JWT configuration strength
- placeholder values in non-local environments
- unsafe Stripe webhook verification bypass outside allowed environments

### Database Initialization

Service applies migrations at startup with retry behavior for delayed database readiness.

### Authorization

Includes role and user-scoped policy support for protected operations.

---

## Workflow Role

Payment service participates in asynchronous order flow by:
- consuming relevant payment-triggering events
- processing payment outcomes
- publishing payment result events for ordering updates

---

## API Areas (High Level)

Typical payment capabilities:
- Payment creation/processing endpoints
- Payment status/history retrieval
- Operational/health endpoints

Gateway and service policies control route protection.

---

## Health and Telemetry

Payment service exposes:
- `/health/ready`
- `/health/live`
- `/prometheus`
- `/metrics`

And emits structured logs, traces, and metrics for payment diagnostics.

---

## Operational Notes

- Keep Stripe and JWT secrets environment-specific and non-placeholder.
- Treat webhook and idempotency behavior as critical regression areas.
- Validate payment state transitions with integration tests.

---

## Related Documents

- [Ordering Service](ordering-service.md)
- [API Gateway](api-gateway.md)
- [Infrastructure - Message Broker](../06-infrastructure/message-broker.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
