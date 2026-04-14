# Design Patterns Used

This document summarizes architecture and implementation patterns used in the current EShop backend.

---

## Domain and Modeling Patterns

### 1) Bounded Contexts

The solution separates business domains into independent services:
- Identity
- Catalog
- Basket
- Ordering
- Payment
- Notification

Each context owns its API, logic, and persistence.

---

### 2) Entity and Value Object Modeling

Domain models use explicit domain concepts (entities/value-like records) to keep business logic expressive and consistent.

**Benefits**
- Clear domain invariants
- Better model readability

---

### 3) Domain Event Dispatching

Domain events are dispatched through infrastructure services and can be translated into integration events.

**Benefits**
- Decoupled side effects
- Better separation between local domain logic and cross-service reactions

---

## Application Layer Patterns

### 4) CQRS-Style Separation

Commands and queries are handled through application handlers, enabling distinct read/write behavior.

**Benefits**
- Clear use-case boundaries
- Better optimization opportunities for reads and writes

---

### 5) Mediator Pattern (MediatR)

Application requests are dispatched via MediatR, reducing controller/endpoint coupling to handlers.

**Benefits**
- Cleaner endpoint code
- Centralized pipeline behavior support

---

### 6) Pipeline Behavior Pattern

Common behaviors are applied in MediatR pipeline, including request validation.

**Observed implementation example**
- FluentValidation-based `ValidationBehavior` in shared building blocks.

**Benefits**
- Uniform validation and cross-cutting execution flow

---

## Infrastructure and Integration Patterns

### 7) Repository and Persistence Abstraction

Infrastructure encapsulates persistence concerns (primarily EF Core + PostgreSQL) behind service-specific abstractions.

**Benefits**
- Separation from API/application concerns
- Testability and replacement flexibility

---

### 8) Database per Service

Each service owns its data store, preventing direct shared write access across contexts.

**Benefits**
- Strong service autonomy
- Reduced cross-team coupling

---

### 9) Event-Driven Integration

RabbitMQ + MassTransit enable asynchronous integration between services.

**Benefits**
- Loose coupling
- Better fault isolation for long-running workflows

---

### 10) Outbox-Oriented Reliability (Integration Event Outbox)

Messaging infrastructure includes integration outbox capabilities to improve event publication reliability.

**Benefits**
- Safer publication flow around transactional boundaries

---

### 11) Resilience Patterns

Resilience features are configured in shared/runtime infrastructure:
- Retry
- Circuit breaker
- Concurrency and endpoint controls

**Benefits**
- Improved stability under transient failures

---

## API and Gateway Patterns

### 12) API Gateway Pattern

Gateway centralizes external entrypoint concerns:
- Route forwarding
- JWT auth policies
- Rate limiting
- Correlation middleware

**Benefits**
- Consistent policy enforcement
- Simplified client-facing surface

---

### 13) Health Check Pattern

Services expose health/readiness/liveness endpoints.

**Benefits**
- Better orchestration readiness
- Faster diagnosis of startup/dependency issues

---

## Observability Patterns

### 14) Structured Logging

Serilog is used for structured logs with centralized collection in Seq.

---

### 15) Distributed Tracing and Metrics

OpenTelemetry instrumentation with collector pipeline and Jaeger tracing; Prometheus/Grafana for metrics and dashboards.

---

## Why These Patterns

The selected pattern set supports:
- Service autonomy
- Predictable cross-service integration
- Maintainable code organization
- Operational visibility and resilience

---

## Related Documents

- [Architecture Decisions](architecture-decisions.md)
- [C4 Diagrams](c4-diagrams.md)
- [Data Flow](data-flow.md)
- [Security Architecture](security-architecture.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
