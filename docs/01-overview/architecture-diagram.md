# Architecture Diagram

## System Architecture (Current)

```
                                     ┌──────────────────────┐
                                     │      API Clients     │
                                     │  (Web, mobile, tools)│
                                     └───────────┬──────────┘
                                                 │
                                     ┌───────────▼──────────┐
                                     │      API Gateway     │
                                     │         (YARP)       │
                                     └───────────┬──────────┘
                                                 │
      ┌──────────────────────────────────────────┼──────────────────────────────────────────┐
      │                                          │                                          │
┌─────▼────────┐  ┌──────────────▼──────────────┐  ┌──────────────▼──────────────┐  ┌──────▼───────┐
│ Identity API │  │        Catalog API          │  │         Basket API          │  │ Ordering API │
└─────┬────────┘  └───────────────┬─────────────┘  └─────────────┬───────────────┘  └───────┬──────┘
      │                           │                              │                          │
┌─────▼────────────┐   ┌──────────▼────────────┐          ┌──────▼──────────┐      ┌────────▼────────────┐
│ identity-postgres│   │ catalog-postgres      │          │ redis           │      │ ordering-postgres   │
└──────────────────┘   └───────────────────────┘          └─────────────────┘      └─────────────────────┘

                                   ┌──────────────────────────────────────────────┐
                                   │              RabbitMQ + MassTransit          │
                                   └───────────────┬──────────────────────────────┘
                                                   │
                              ┌────────────────────┼────────────────────┐
                              │                    │                    │
                    ┌─────────▼─────────┐  ┌──────▼───────────┐  ┌──────▼────────────┐
                    │   Payment API     │  │ Notification API │  │ Stripe Listener   │
                    └─────────┬─────────┘  └────────┬─────────┘  └───────────────────┘
                              │                     │
                    ┌─────────▼─────────────┐  ┌─────▼────────────────┐
                    │ payment-postgres      │  │ notification-postgres│
                    └───────────────────────┘  └──────────────────────┘

                                   ┌──────────────────────────────────────────────┐
                                   │                Observability                 │
                                   │ Seq • Prometheus • Grafana • OTEL • Jaeger   │
                                   └──────────────────────────────────────────────┘
```

---

## Component Summary

### Client Access Layer
- API consumers communicate only through the **API Gateway**.
- The gateway centralizes authentication, authorization, routing, rate limiting, and cross-cutting middleware.

### Gateway Layer
**API Gateway (YARP)**
- Routes requests to all backend services.
- Enforces JWT-based policies (`Authenticated`, `Admin`) on protected routes.
- Applies rate limiting and correlation middleware.
- Exposes health and metrics endpoints.

### Domain Service Layer

#### Identity Service
- Manages authentication and account access.
- Issues and validates JWT-compatible identity flows.
- Stores data in dedicated PostgreSQL database.

#### Catalog Service
- Exposes product and category APIs.
- Supports both public read paths and admin write paths (via gateway policies).
- Persists data in dedicated PostgreSQL database.

#### Basket Service
- Manages user basket state.
- Uses Redis as primary storage.
- Participates in checkout workflow that leads into ordering.

#### Ordering Service
- Owns order creation and lifecycle operations.
- Persists orders in dedicated PostgreSQL database.
- Integrates with payment and notification flows through asynchronous events.

#### Payment Service
- Processes payment operations and outcomes.
- Includes Stripe-oriented integration paths and webhook listener support in runtime stack.
- Persists payment data in dedicated PostgreSQL database.

#### Notification Service
- Handles outbound notifications.
- Persists notification-related records in dedicated PostgreSQL database.
- Subscribes to business events relevant to customer communication.

### Messaging Layer

#### RabbitMQ + MassTransit
- Provides asynchronous service integration.
- Decouples service lifecycles and improves fault isolation for long-running flows.

### Data Layer

#### PostgreSQL Databases
- Separate instances/databases are used per bounded context (identity, catalog, ordering, payment, notification).

#### Redis
- Used for basket persistence and additional cache-oriented scenarios.

### Observability Layer

#### Logging
- Serilog emits structured logs.
- Seq provides centralized ingestion and querying.

#### Metrics
- Prometheus scrapes service and exporter metrics.
- Grafana dashboards visualize runtime health and trends.

#### Tracing
- Services emit OpenTelemetry traces.
- OTEL Collector forwards telemetry to Jaeger for distributed trace analysis.

---

## High-Level Request Flow

1. Client sends request to API Gateway.
2. Gateway applies policies and forwards request to target service.
3. Service handles data access (PostgreSQL or Redis).
4. Service emits events when cross-context processing is required.
5. Downstream services consume events and continue workflow.
6. Telemetry is emitted throughout the flow to logs, metrics, and traces.

---

## Related Documents

- [Project Overview](project-overview.md)
- [Technology Stack](tech-stack.md)
- [API Gateway Service Details](../05-services/api-gateway.md)
- [Infrastructure Details](../06-infrastructure/)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
