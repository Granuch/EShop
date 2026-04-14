# Identity Service

Authentication and authorization service for backend APIs.

---

## Overview

Identity Service provides:
- User registration and login flows
- JWT token generation and validation support
- Refresh-token lifecycle support
- Account and role management endpoints
- Internal service authorization support for selected internal calls
- Security-focused startup validation for non-local environments
- Health and telemetry integration

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | API host |
| Identity framework | ASP.NET Core Identity | User/role management |
| Database | PostgreSQL | Identity persistence |
| Cache/protection | Redis | Distributed cache and protection paths |
| Messaging | RabbitMQ + MassTransit | Event integration |
| Validation | FluentValidation + MediatR pipeline | Request validation |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

---

## Project Structure

Identity service follows layered architecture:

- `EShop.Identity.API`
- `EShop.Identity.Application`
- `EShop.Identity.Domain`
- `EShop.Identity.Infrastructure`

---

## Runtime Characteristics

### Security Configuration Guards

Identity startup validates critical configuration, including:
- JWT secret strength
- Placeholder detection in non-local environments
- Internal service API key requirements in non-local environments

### Distributed Cache

Redis is used when configured (non-testing), with in-memory fallback for testing/local fallback scenarios.

### Messaging

MassTransit + RabbitMQ integration is enabled for asynchronous identity-related events.

---

## API Areas (High Level)

Common endpoint groups include:
- Auth endpoints (sign-in, token lifecycle)
- Account endpoints (profile/password/identity operations)
- Role/admin endpoints
- Internal account lookup endpoints for trusted service calls

Gateway policy controls determine external accessibility for protected paths.

---

## Authorization Model

- JWT bearer authentication
- Role-based and policy-based access checks
- Additional internal API key checks for designated internal scenarios

---

## Health and Telemetry

Identity service exposes health endpoints and emits:
- Structured logs
- OpenTelemetry traces/metrics
- Prometheus-compatible metrics endpoints

---

## Operational Notes

- Keep JWT and internal auth settings environment-specific and non-placeholder.
- Keep role/policy definitions aligned with gateway route authorization.
- Treat token and account operations as security-sensitive change areas requiring tests.

---

## Related Documents

- [API Gateway](api-gateway.md)
- [Ordering Service](ordering-service.md)
- [Infrastructure - Security and Resilience](../06-infrastructure/resilience.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
