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

Common behaviors are applied in the MediatR pipeline, and **registration order sets execution
order** — the pipeline runs in the order behaviors are added in each service's `Program.cs`, so
the four `Add*` calls that wire it up are themselves architecturally significant, not just DI
plumbing.

**Behaviors, in the order they run for a service with the full set** (Audit → CacheInvalidation →
Transaction → Validation → Logging → Caching → handler):
- **`AuditBehavior`**: writes one `audit_log` row per audited command (`Succeeded`/
  `Rejected`/`Failed`), through its own database scope so a rolled-back command still produces a
  row. Registered outermost so it observes the true outcome even if a later behavior throws.
  Commands opt in via `IAuditedCommand`; every command an admin-authorized endpoint can send is
  classified as audited or explicitly not, and a new command fails a structural test until it is
  classified one way or the other.
- **`CacheInvalidationBehavior`**: evicts or bumps declared cache keys/families. Must run
  **before** `TransactionBehavior` (i.e. outside the transaction) — evicting inside the
  transaction would remove a cache entry before the write that should invalidate it has committed.
- **`TransactionBehavior`**: opens the unit of work. Commits on **any non-exception return,
  including a `Result` failure** — a handler that writes state and then returns a `Result` failure
  still commits that write, which is a standing trap for anything implementing
  `ITransactionalCommand`.
- **`ValidationBehavior`**: FluentValidation. For the generic `Result<T>` response type, a
  validation failure becomes a `Result` carrying a `FieldValidationError`; for the non-generic
  `Result`, it throws a `ValidationException` instead, which the shared exception middleware maps.
  Both paths answer the same 400 `ValidationError` with an `errors` map keyed by camelCase field
  name, so the handler's `Result` shape no longer shows on the wire.
- **`LoggingBehavior`**: logs the request at Information, redacting members flagged
  `[SensitiveData]` or matching a hardcoded property-name list (`Password`, `Token`, `Code`, …).
- **`CachingBehavior`**: read-side caching for `ICacheableQuery` handlers.

**Benefits**
- Uniform validation, auditing and transactional execution flow across services

**Risks**
- A registration-order mistake is invisible to functional tests — nothing fails, nothing logs —
  and is only caught by a structural test that resolves and compares the registered behavior
  order (`BehaviorOrderTests`/`AuditRunsOutermost` in each service's integration suite).

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

## Authorization Patterns

### 16) Layered Role and Permission Authorization

Two authorization layers apply to every admin-panel request: a coarse check at the API
Gateway (route-level `AdminArea` policy: the caller holds at least one permission) and a
fine-grained permission check in the owning service
(`EShopPermissions`, a named policy per permission; `Admin` bundles all 15). A caller satisfies the
service-level check via either an explicit `permission` claim or an `Admin` role claim, so existing
role-based tokens remain valid without reissue.

**Benefits**
- New admin capabilities can be gated more precisely than "is this user an Admin" without
  reworking existing tokens or the gateway route table.

**Risks**
- The two layers can drift: a service can add an admin endpoint under a storefront prefix without
  the gateway gaining a matching `AdminArea` route. Each service's integration suite now checks its
  admin endpoints against the gateway's route table, which is what caught up Ordering's and
  Payment's admin surfaces and Catalog's category stats (see
  [Security Architecture](security-architecture.md#permission-model)).

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
- [Frontend API Contracts — Conventions](../01-overview/frontend/conventions.md)

---

**Version**: 2.1  
**Last Updated**: 2026-09-26
