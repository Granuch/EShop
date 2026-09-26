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

## ADR-008: Layer a Fine-Grained Permission Model Under Role-Based Authorization

**Status**: Accepted  
**Date**: 2026-09-26  
**Deciders**: Core maintainers

### Context

ADR-005 established JWT authentication with role-based route policies (`Authenticated`, `Admin`)
enforced at the gateway and re-checked in each service. The admin panel work (Identity's admin
users API onward) needed access control finer than "is this caller an Admin" — for example, an
operator who can read payment data but not settle or refund it, or a support role that can read
user accounts but not modify roles — without breaking every already-issued token or requiring a
new claim type per capability.

### Decision

Introduce a named permission model, `EShopPermissions` (`BuildingBlocks.Infrastructure/Authorization/`):
15 permissions (`users.read`, `users.manage`, `payments.write`, `system.manage`, `audit.read`, …),
where the permission string **is** the ASP.NET Core policy name, so there is no second table that
can drift out of sync with the code. `RolePermissionBundles` maps the existing `Admin` role to all
15 permissions. A caller is authorized for a permission if they present either an explicit
`permission` claim **or** a role claim with a bundle that includes it — so every existing
role-based token keeps working unmodified. `AddEShopPermissions()` is called in all seven
components (the gateway and all six services). The gateway's route-level policy stays
role-based (`Admin`); the service layer is where a permission is actually checked.

### Consequences

**Positive:**
- New admin capabilities can be scoped more precisely than an all-or-nothing `Admin` role, without
  reissuing tokens or touching the gateway's route table.
- The permission string doubling as the policy name removes an entire class of "the permission
  name in code doesn't match the string in the docs/database" drift.
- Existing role-based tokens and the `Admin` role continue to work with no migration.

**Negative:**
- Two authorization layers (gateway role gate, service permission check) must now be kept
  consistent by hand for every new admin endpoint; nothing enforces that a service-level
  permission also gets a matching gateway-level route policy.
- No component issues a `permission` claim today. A client learns what it may do from the
  `permissions` list on the login response and on `GET /api/v1/account/profile`, which Identity
  derives from the caller's roles through the same `RolePermissionBundles` table the services
  authorize against (added 2026-09-27, frontend-contracts F-07). Until then a client could only
  infer capabilities from `user.roles`.

**Risks:**
- **Realized, not merely theoretical**: Ordering's and Payment's entire admin surface, and
  Catalog's `GET /categories/{id}/stats`, have a service-level permission/role check but no
  matching gateway-level `Admin` route — the redundant defense-in-depth every other admin surface
  gets is absent there. No live bypass has been found (the service backstops every case tried),
  but the gap is structural, not just an unlucky endpoint. See
  [Security Architecture — Permission Model](security-architecture.md#permission-model).
- A role deletion or role-membership change is not retroactive on an already-issued access token,
  and a deleted role's claim can persist in a cached role list for up to 5 minutes after deletion —
  a caller can act on a permission bundle they were just stripped of, for a bounded window.

### Alternatives Considered

#### Replace roles with permissions entirely
Pros: one authorization model instead of two overlapping ones.  
Cons: a breaking change for every already-issued token and every client reading `user.roles`; no
incremental adoption path for the admin panel's staged rollout.

#### Per-endpoint ad-hoc policy names, no central permission catalogue
Pros: no upfront design, fastest to add a single endpoint.  
Cons: the exact failure mode `RolePermissionBundles` was built to avoid — policy-name drift between
services, and no single place to see what `Admin` actually grants.

---

## References

- [Microservices.io patterns](https://microservices.io/)
- [C4 Model](https://c4model.com/)
- [MassTransit](https://masstransit.io/)
- [OpenTelemetry](https://opentelemetry.io/)

---

**Version**: 2.1  
**Last Updated**: 2026-09-26
