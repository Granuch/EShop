# Notification Service

Event-driven notification service for outbound communication workflows.

---

## Overview

Notification Service provides:
- Event consumption for business-triggered notifications
- Email delivery pipeline through configured SMTP settings
- Notification persistence and processing support
- Environment-aware configuration safeguards
- Health and telemetry integration

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | API host/background processing |
| Database | PostgreSQL | Notification data persistence |
| Messaging | RabbitMQ + MassTransit | Event consumption |
| Email transport | SMTP provider (Mailpit/local or external SMTP) | Delivery channel |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

---

## Project Structure

Notification service follows layered architecture:

- `EShop.Notification.API`
- `EShop.Notification.Application`
- `EShop.Notification.Domain`
- `EShop.Notification.Infrastructure`

---

## Runtime Characteristics

### Startup Guards

Notification startup validates required settings (for example password-reset URL requirements and environment constraints).

### Database Initialization

Service applies migrations at startup with retry logic for transient database readiness conditions.

### Messaging Integration

MassTransit consumers process notification-relevant events from the message broker.

---

## Workflow Role

Notification service reacts to events emitted by other services and handles asynchronous delivery logic rather than blocking upstream request paths.

---

## Health and Telemetry

Notification service exposes:
- `/health/ready`
- `/health/live`
- `/prometheus`
- `/metrics`

And emits structured logs, traces, and metrics for operational diagnostics.

---

## Operational Notes

- Keep SMTP settings environment-specific and validated.
- Monitor consumer failures and delivery backlogs.
- Treat notification templates/content mapping as contract-sensitive behavior.

---

## Related Documents

- [Ordering Service](ordering-service.md)
- [Identity Service](identity-service.md)
- [Infrastructure - Message Broker](../06-infrastructure/message-broker.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
