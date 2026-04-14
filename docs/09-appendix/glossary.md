# Glossary

Key terms used in this documentation set.

---

## A

### API Gateway
Single ingress service that routes and enforces policies before forwarding requests to backend services.

### Aggregate
Domain-driven design pattern representing a consistency boundary around related domain objects.

### Authorization Policy
Named rule set used to restrict access to routes or operations.

---

## B

### Bounded Context
Domain boundary where a specific model and language apply.

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

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
