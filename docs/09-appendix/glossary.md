# Glossary

Key terms used in this documentation set.

---

## A

### API Gateway
Single ingress service that routes and enforces policies before forwarding requests to backend services.

### Aggregate
Domain-driven design pattern representing a consistency boundary around related domain objects.

### Audit Log
A per-service `audit_log` table (plus the gateway's merged `GET /api/v1/admin/audit` view) recording
one row per audited admin command, with its outcome (`Succeeded`/`Rejected`/`Failed`), the calling
actor, and a redacted copy of the request payload. Written by `AuditBehavior`, the outermost
MediatR pipeline behavior. See [Design Patterns](../03-architecture/design-patterns.md) and
[`frontend/admin-platform.md`](../01-overview/frontend/admin-platform.md).

### Authorization Policy
Named rule set used to restrict access to routes or operations. See also **Permission** below —
this repository layers named permissions under role-based policies for admin endpoints.

---

## B

### Bounded Context
Domain boundary where a specific model and language apply.

### Bulk Report
The response shape for a bulk admin operation (e.g. Catalog's bulk publish/unpublish/delete,
import), giving one per-item result rather than a single pass/fail for the whole request — so a
batch of 100 ids can report 97 successes and 3 named failures in one response. See
[`frontend/catalog.md`](../01-overview/frontend/catalog.md).

### Build Pipeline
Automated process that restores, builds, and validates code changes.

---

## C

### Cache-Aside
Caching approach where data is loaded into cache on read miss and invalidated/updated on writes.

### Circuit Breaker
Resilience pattern that temporarily blocks calls to failing dependencies.

### CQRS
Separation of command (write) and query (read) responsibilities.

### Correlation ID
Identifier propagated across calls to trace a request through distributed services.

---

## D

### Database per Service
Pattern where each service owns its data store and schema evolution.

### Dead-Letter Behavior
Handling path for messages that repeatedly fail processing.

### Distributed Tracing
Telemetry model that tracks request flow across multiple services.

---

## E

### End-to-End Workflow Test
Validation of multi-service business flow behavior from entrypoint to final state.

### Event-Driven Integration
Asynchronous service communication using published and consumed events.

### Eventual Consistency
Consistency model where related service state converges over time instead of immediately.

---

## F

### Fail Fast
Approach of rejecting invalid configuration/state early during startup or execution.

---

## G

### Gateway Policy
Route-level rule enforced by the API Gateway (for example authenticated or admin access).

---

## H

### Health Check
Runtime endpoint used to indicate service status.

### Hot Path
Code path frequently executed and often performance-sensitive.

---

## I

### Idempotency
Property where repeated processing of the same request/event yields the same logical result.

### Integration Test
Test validating interactions among real components (API, persistence, messaging, etc.).

---

## J

### JWT
JSON Web Token used for stateless authentication/authorization.

---

## L

### Liveness Probe
Health signal indicating whether a service process is running.

---

## M

### MassTransit
Messaging framework used to integrate with RabbitMQ and implement consumer/publisher patterns.

### Metrics
Numeric telemetry values used for trend and alert analysis.

### Middleware
Pipeline component that processes requests/responses in ASP.NET Core.

---

## O

### OpenTelemetry
Open standard for collecting traces, metrics, and telemetry metadata.

### Outbox Pattern
Reliability pattern for safely persisting and later publishing integration events.

---

## P

### Permission
A fine-grained access right (`EShopPermissions`, 15 in total, e.g. `users.manage`,
`payments.write`) checked by a service in addition to the gateway's coarser `AdminArea` route
policy, which only asks whether the caller holds any permission. The `Admin` role is bundled with all 15, so existing role-based tokens keep working; a
caller can also be granted a single permission via a `permission` claim, though no component today
issues one. A client reads what it holds from `permissions` on the login response and the profile. See
[Security Architecture](../03-architecture/security-architecture.md#permission-model).

### ProblemDetails / errorCode
The RFC 7807-style error envelope this platform's non-2xx responses use
(`type`/`title`/`status`/`detail`/`traceId`), extended with a custom `errorCode` field in the
`Service.Reason` style (e.g. `Product.NotFound`) that a client should switch on instead of
`status` or `detail` text. Payment is the one exception, using `SCREAMING_SNAKE` codes instead.
See [`frontend/conventions.md#3-errors`](../01-overview/frontend/conventions.md#3-errors).

### Prometheus
Metrics backend that scrapes and stores time-series telemetry.

### Readiness Probe
Health signal indicating whether a service is ready to accept traffic.

---

## R

### RabbitMQ
Message broker used for asynchronous integration between services.

### Rate Limiting
Traffic control mechanism that restricts request volume per key/window.

### Retry Policy
Policy that retries operations after transient failure.

---

## S

### Sandbox Environment
Local/development-like runtime mode with convenient defaults for contributor workflows.

### Seq
Centralized structured logging server used for log search and diagnostics.

### Service Boundary
Architectural boundary that defines ownership of API, logic, and data for a service.

---

## T

### Trace
Single end-to-end record of related operations across distributed services.

### TTL (Time to Live)
Expiration period for cached values or temporary state.

---

## U

### Unit Test
Test that validates isolated logic without real external dependencies.

---

## V

### Value Object
Domain object defined by value equality rather than identity.

---

## Related Documents

- [Architecture Decisions](../03-architecture/architecture-decisions.md)
- [Services](../05-services/)
- [Infrastructure](../06-infrastructure/)
- [Frontend API Contracts](../01-overview/frontend/README.md)

---

**Version**: 2.1  
**Last Updated**: 2026-09-26
