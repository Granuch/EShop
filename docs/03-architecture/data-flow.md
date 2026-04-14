# Data Flow and Communication Patterns

This document describes how requests, events, and data move through the current EShop backend platform.

---

## Communication Types

### 1) Synchronous Communication (HTTP)

Used when the caller needs an immediate response.

Typical paths:
- Client -> API Gateway -> target API
- Gateway health and readiness checks
- Service-level external provider calls (for example payment or SMTP interactions)

**Strengths**
- Simple request/response model
- Immediate result for caller

**Trade-offs**
- Runtime coupling between caller and callee
- Higher cascading-failure risk if dependencies are unavailable

---

### 2) Asynchronous Communication (RabbitMQ + MassTransit)

Used for decoupled cross-service workflows.

Typical paths:
- Basket/ordering/payment progression via integration events
- Notification trigger events

**Strengths**
- Service decoupling
- Better tolerance for temporary downstream outages

**Trade-offs**
- Eventual consistency
- More complex end-to-end debugging

---

## Request Flow Examples

### Example 1: Authenticated API Request Through Gateway

```
Client -> API Gateway : HTTP request + Bearer token
API Gateway : validate JWT + route policy
API Gateway -> Target API : forward request
Target API -> Database/Redis : process business data
Target API -> API Gateway -> Client : response
```

Highlights:
- Gateway applies authentication/authorization before forwarding.
- Correlation and logging middleware support diagnostics.

---

### Example 2: Basket Checkout to Order/Payment Path

```
Client -> Gateway -> Basket API : checkout request
Basket API -> Redis             : read/update basket state
Basket API -> RabbitMQ          : publish checkout event
Ordering API <- RabbitMQ        : consume event, create order
Ordering API -> ordering DB     : persist order
Ordering API -> RabbitMQ        : publish next-step events
Payment API <- RabbitMQ         : consume payment-related event
Payment API -> payment DB       : persist payment state
Notification API <- RabbitMQ    : consume notification events
```

Highlights:
- User-facing call returns quickly.
- Downstream processing continues asynchronously.
- Failures are handled with retry/circuit-breaker and queue-based recovery behavior.

---

### Example 3: Product Read Path with Cache

```
Client -> Gateway -> Catalog API : GET products
Catalog API -> Redis             : cache lookup
Redis hit? yes -> return cached response
Redis hit? no  -> query catalog DB -> update cache -> return response
```

Highlights:
- Cache hits reduce database load and latency.
- TTL and invalidation strategy determine freshness.

---

## Event Flow (High-Level)

```
Domain action in Service A
  -> integration event published to RabbitMQ
  -> consumed by one or more services
  -> each consumer applies its own transactional logic
  -> optional follow-up event publication
```

This supports independent service evolution while preserving business process continuity.

---

## Communication Matrix

| Source | Target | Channel | Pattern |
|--------|--------|---------|---------|
| Client | API Gateway | HTTP | Sync |
| API Gateway | Domain APIs | HTTP | Sync |
| Domain APIs | PostgreSQL | SQL | Sync |
| Domain APIs | Redis | Redis protocol | Sync |
| Domain APIs | RabbitMQ | AMQP | Async |
| RabbitMQ | Consumers | AMQP | Async |
| Services | Seq/Prometheus/OTEL | Telemetry protocols | Async/stream |

---

## Consistency Model

### Strong consistency (within a service boundary)

- Service transaction against its own database.
- Single-unit commit for local domain changes.

### Eventual consistency (across service boundaries)

- Changes are propagated via events.
- Other services update state asynchronously.

This is a deliberate trade-off in the current microservices model.

---

## Reliability and Resilience Behavior

Current runtime uses resilience-oriented settings and middleware patterns including:
- Retry behavior for transient failures
- Circuit-breaker behavior for unstable dependencies
- Health/readiness/liveness checks
- Structured logging and correlation IDs

---

## Observability of Data Flow

To investigate end-to-end behavior:
- Logs in Seq (structured and searchable)
- Metrics in Prometheus/Grafana
- Traces in Jaeger via OpenTelemetry Collector

Use correlation IDs and trace context to follow a single request/event path across gateway and services.

---

## Summary

| Pattern | Primary Use | Consistency |
|---------|-------------|------------|
| Sync HTTP | Immediate API response | Strong in local operation scope |
| Async events | Cross-service workflows | Eventual |
| Redis cache | Fast reads and state lookup | Eventual |

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
