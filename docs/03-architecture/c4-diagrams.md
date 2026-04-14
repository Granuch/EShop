# C4 Model Diagrams

This document provides C4-style architecture views for the current EShop backend platform.

---

## Level 1: System Context

```
┌────────────────────────────────────────────────────────────────────┐
│                           System Context                           │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌───────────────────────┐                                         │
│  │ API Client            │                                         │
│  │ (Web/mobile/tooling)  │                                         │
│  └───────────┬───────────┘                                         │
│              │ Uses HTTPS APIs                                     │
│              ▼                                                     │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ EShop Backend Platform                                      │   │
│  │ - API Gateway + domain services                             │   │
│  │ - Event-driven integration                                  │   │
│  └───────────┬───────────────────────────────┬─────────────────┘   │
│              │                               │                     │
│              │ Uses                          │ Uses                │
│              ▼                               ▼                     │
│     ┌──────────────────┐            ┌─────────────────────┐        │
│     │ Stripe           │            │ SMTP provider /     │        │
│     │ (payment flows)  │            │ local Mailpit       │        │
│     └──────────────────┘            └─────────────────────┘        │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

---

## Level 2: Container Diagram

```
┌────────────────────────────────────────────────────────────────────┐
│                          Container Diagram                         │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  Client                                                            │
│    │ HTTPS                                                         │
│    ▼                                                               │
│  API Gateway (YARP, ASP.NET Core)                                  │
│    │                                                               │
│    ├────────► Identity API                                         │
│    ├────────► Catalog API                                          │
│    ├────────► Basket API                                           │
│    ├────────► Ordering API                                         │
│    ├────────► Payment API                                          │
│    └────────► Notification API                                     │
│                                                                    │
│  Shared runtime dependencies:                                      │
│    - RabbitMQ + MassTransit                                        │
│    - PostgreSQL (service-owned databases)                          │
│    - Redis (basket/cache)                                          │
│                                                                    │
│  Observability stack:                                              │
│    - Serilog + Seq                                                 │
│    - Prometheus + Grafana                                          │
│    - OpenTelemetry + Collector + Jaeger                            │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

### Containers

| Container | Technology | Responsibility |
|----------|------------|----------------|
| API Gateway | ASP.NET Core + YARP | Routing, auth policies, rate limiting, gateway middleware |
| Identity API | ASP.NET Core | Auth and account operations |
| Catalog API | ASP.NET Core | Product/category operations |
| Basket API | ASP.NET Core | Basket lifecycle and checkout trigger |
| Ordering API | ASP.NET Core | Order lifecycle management |
| Payment API | ASP.NET Core | Payment processing and integration |
| Notification API | ASP.NET Core | Notification workflows |
| RabbitMQ | RabbitMQ 3.13 | Async transport |
| Redis | Redis 7 | Basket persistence and cache scenarios |
| PostgreSQL instances | PostgreSQL 16 | Transactional service data |
| Seq | Seq | Centralized logs |
| Prometheus | Prometheus | Metrics scraping |
| Grafana | Grafana | Dashboard visualization |
| OTEL Collector | OpenTelemetry Collector | Telemetry processing pipeline |
| Jaeger | Jaeger | Trace analysis |

---

## Level 3: Component Diagram (API Gateway)

```
┌────────────────────────────────────────────────────────────────────┐
│                      API Gateway Components                        │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │ AuthN/AuthZ                                                  │  │
│  │ - JWT validation                                             │  │
│  │ - Route policies (Authenticated/Admin)                       │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │ Reverse Proxy Routing (YARP)                                 │  │
│  │ - Route table / clusters                                     │  │
│  │ - Downstream forwarding                                      │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │ Cross-cutting Middleware                                     │  │
│  │ - Correlation ID                                             │  │
│  │ - Error handling                                             │  │
│  │ - Rate limiting                                              │  │
│  │ - Optional simulation middleware                             │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │ Operational Endpoints                                        │  │
│  │ - /health, /health/ready, /health/live                       │  │
│  │ - /prometheus and OpenTelemetry metrics                      │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

---

## Level 4: Code Diagram (Request Pipeline View)

```
Client Request
   │
   ▼
API Gateway
   ├─ Global exception handling
   ├─ Request logging + correlation middleware
   ├─ CORS + rate limiter
   ├─ Authentication + authorization
   ├─ Proxy guard middlewares
   ├─ Optional simulation middlewares
   └─ YARP route forwarding
         │
         ▼
Target Service API
   ├─ Auth validation (where required)
   ├─ Endpoint handler/controller
   ├─ Application pipeline behaviors (validation, etc.)
   ├─ Domain/application logic
   └─ Infrastructure access (PostgreSQL/Redis/RabbitMQ)
```

---

## Deployment View (Current Local Runtime)

```
Docker Compose (root)
  - sandbox profile:
      api-gateway, identity-api, catalog-api, basket-api,
      ordering-api, payment-api, notification-api,
      postgres instances, redis, rabbitmq, mailpit

  - monitoring profile:
      seq, prometheus, grafana, jaeger, otel-collector,
      exporters
```

---

## Sequence Diagram: Checkout to Order Progression

```
Client -> Gateway -> Basket API : POST /api/v1/basket/checkout
Basket API -> Redis            : read basket
Basket API -> RabbitMQ         : publish checkout event
Ordering API <- RabbitMQ       : consume checkout event
Ordering API -> ordering DB    : create order
Ordering API -> RabbitMQ       : publish order-created/payment-required events
Payment API <- RabbitMQ        : consume payment event
Payment API -> payment DB      : persist payment state
Notification API <- RabbitMQ   : consume notification events
```

---

## Diagram Tooling

- Structurizr
- PlantUML (including C4-PlantUML)
- Draw.io or Excalidraw

---

## References

- [C4 Model](https://c4model.com/)
- [Structurizr](https://structurizr.com/)
- [PlantUML C4](https://github.com/plantuml-stdlib/C4-PlantUML)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
