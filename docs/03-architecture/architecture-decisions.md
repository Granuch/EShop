# Architecture Decision Records (ADRs)

This document captures key architecture decisions for the current EShop backend platform.

**Format**: MADR-style markdown records.

---

## ADR Template

```markdown
# ADR-XXX: [Title]

**Status**: [Proposed | Accepted | Deprecated | Superseded]  
**Date**: YYYY-MM-DD  
**Deciders**: [Names or Roles]  

## Context

[Problem statement and constraints]

## Decision

[Chosen approach]

## Consequences

**Positive:**
- ...

**Negative:**
- ...

**Risks:**
- ...

## Alternatives Considered

### Alternative 1
Pros: ...  
Cons: ...

### Alternative 2
Pros: ...  
Cons: ...
```

---

## ADR-001: Use Domain-Oriented Microservices with API Gateway

**Status**: Accepted  
**Date**: 2026-04-14  
**Deciders**: Core maintainers

### Context

The platform is organized around independent business domains and must support isolated development, deployment, and fault boundaries.

### Decision

Use domain-oriented microservices:
- Identity
- Catalog
- Basket
- Ordering
- Payment
- Notification
- API Gateway (single entrypoint)

### Consequences

**Positive:**
- Independent service lifecycle management.
- Clear ownership boundaries by domain.
- Better fault isolation and selective scaling.

**Negative:**
- Increased distributed-system complexity.
- More operational surface area.

**Risks:**
- Contract drift across services.
- Higher integration-test burden.

### Alternatives Considered

#### Modular Monolith
Pros: simpler deployment and debugging.  
Cons: weaker service isolation and scaling flexibility.

#### Classic Monolith
Pros: low initial complexity.  
Cons: high coupling and slower independent evolution.

---

## ADR-002: Use Layered Service Architecture with Shared Building Blocks

**Status**: Accepted  
**Date**: 2026-04-14

### Context

Services require consistent internal structure and reusable cross-cutting behavior.

### Decision

Adopt layered service architecture with shared `BuildingBlocks` projects:
- Domain
- Application
- Infrastructure
- API

### Consequences

**Positive:**
- Consistent implementation model across services.
- Reuse of common behaviors (validation, messaging, telemetry helpers).
- Clear separation of concerns.

**Negative:**
- Additional boilerplate.
- Requires discipline to avoid layer leaks.

**Risks:**
- Over-abstraction in simple features.

### Alternatives Considered

#### Per-service ad-hoc structure
Pros: fast short-term coding.  
Cons: uneven quality and higher long-term maintenance cost.

---

## ADR-003: Use PostgreSQL per Service + Redis for Basket/Caching

**Status**: Accepted  
**Date**: 2026-04-14

### Context

The system needs transactional persistence with strict domain ownership boundaries and low-latency basket/cache support.

### Decision

- PostgreSQL is primary persistence for identity, catalog, ordering, payment, and notification contexts.
- Redis is used for basket storage and cache-oriented scenarios.
- Data ownership follows service boundaries.

### Consequences

**Positive:**
- Strong domain ownership and fewer direct cross-service data dependencies.
- Proven relational support for transactional workloads.
- Fast basket and cache operations.

**Negative:**
- Data duplication between contexts when required for autonomy.
- Cross-service consistency is eventual, not immediate.

**Risks:**
- Stale replicated data if integration events are delayed.

### Alternatives Considered

#### Shared database across services
Pros: easy joins and single source in one place.  
Cons: strong coupling and broken service autonomy.

#### NoSQL-first approach for all domains
Pros: horizontal write scaling in some scenarios.  
Cons: weaker fit for transactional relational workflows.

---

## ADR-004: Use RabbitMQ + MassTransit for Asynchronous Integration

**Status**: Accepted  
**Date**: 2026-04-14

### Context

Cross-service workflows require reliable asynchronous communication and resilience features.

### Decision

Use RabbitMQ transport with MassTransit abstraction and standardized endpoint configuration.

### Consequences

**Positive:**
- Decoupled event-driven workflows.
- Built-in retry/circuit-breaker options and endpoint conventions.
- Better resilience under partial failures.

**Negative:**
- More complex debugging than direct HTTP-only integration.
- Eventual consistency management required.

**Risks:**
- Duplicate message processing without idempotent handling.

### Alternatives Considered

#### HTTP-only service orchestration
Pros: simple request tracing per call.  
Cons: tight runtime coupling and cascading-failure risk.

#### Kafka-first stack
Pros: high-throughput event streaming.  
Cons: unnecessary complexity for current solution scope.

---

## ADR-005: Use JWT-Based Auth at Gateway and Services

**Status**: Accepted  
**Date**: 2026-04-14

### Context

The platform requires stateless authentication and route-level authorization controls.

### Decision

Use JWT authentication with issuer/audience validation and role/policy authorization. Gateway enforces access policies on routed endpoints.

### Consequences

**Positive:**
- Stateless auth model across services.
- Centralized policy enforcement at gateway.
- Standardized token validation behavior.

**Negative:**
- Token revocation strategy must be explicit.
- Strict secret management is mandatory.

**Risks:**
- Misconfigured keys or token settings can impact all downstream APIs.

### Alternatives Considered

#### Stateful session-based auth
Pros: straightforward token revocation.  
Cons: less suitable for distributed stateless API topology.

---

## ADR-006: Use OpenTelemetry-Centric Observability Stack

**Status**: Accepted  
**Date**: 2026-04-14

### Context

Distributed services require unified diagnostics for logs, metrics, and traces.

### Decision

Use:
- Serilog + Seq for structured logs
- Prometheus + Grafana for metrics
- OpenTelemetry + Collector + Jaeger for traces

### Consequences

**Positive:**
- End-to-end visibility for request and message flows.
- Consistent telemetry instrumentation model.
- Faster incident diagnosis.

**Negative:**
- Additional runtime components to manage.

**Risks:**
- Misconfigured sampling or retention may hide issues.

### Alternatives Considered

#### Logs-only approach
Pros: low setup complexity.  
Cons: insufficient insight for distributed tracing and latency analysis.

---

## ADR-007: Keep Convenient Local Secrets, Harden Non-Local Environments

**Status**: Accepted  
**Date**: 2026-04-14

### Context

Contributors need fast local onboarding while preserving production security posture.

### Decision

- Allow convenient password-based local values in `.env` and local config files.
- Enforce placeholder detection and strict validation in non-development environments.
- Never commit real production secrets.

### Consequences

**Positive:**
- Fast local setup.
- Explicit guardrails for higher environments.

**Negative:**
- Requires careful environment separation.

**Risks:**
- Accidental reuse of local defaults outside local scope.

### Alternatives Considered

#### Strict secret manager requirement for all environments
Pros: maximal security baseline.  
Cons: slower onboarding and higher barrier for contributors.

---

## References

- [Microservices.io patterns](https://microservices.io/)
- [C4 Model](https://c4model.com/)
- [MassTransit](https://masstransit.io/)
- [OpenTelemetry](https://opentelemetry.io/)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
