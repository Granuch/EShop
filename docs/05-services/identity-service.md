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

### Seed Data

On startup, after migrations, Identity seeds baseline data via `SeedData`
(`EShop.Identity.Infrastructure/Data/SeedData.cs`):

- **Roles** (`Admin`, `User`) are seeded in every non-Testing environment via `SeedRolesAsync`.
- **Admin user** is additionally seeded in Development and Sandbox via `SeedRolesAndAdminAsync`, using:
  - `Identity:SeedAdminEmail` (env: `IDENTITY_SEED_ADMIN_EMAIL`, default `admin@eshop.com`)
  - `Identity:SeedAdminPassword` (env: `IDENTITY_SEED_ADMIN_PASSWORD`, default `Admin123!`) — if unset, admin
    seeding is skipped with a warning log; roles are still seeded regardless.
- Idempotent: existing roles/admin user are matched by name/email and left untouched on
  subsequent startups.

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

**Full endpoint-by-endpoint contracts (routes, both auth layers, request/response shapes, error
tables, TypeScript types) live in
[frontend/identity.md](../01-overview/frontend/identity.md), the authoritative source. This
section is a summary, not a duplicate.**

42 endpoints in four groups:
- **Auth** (`/api/v1/auth/*`, anonymous, rate limits `auth`/`login`): register, login (with a
  `requires2FA` branch), refresh/revoke-token, confirm-email, forgot/reset-password.
- **Account** (`/api/v1/account/*`, any signed-in user): profile GET/PUT, change-password,
  enable/verify/disable 2FA.
- **Admin users** (`/api/v1/admin/users/*`, gateway `Admin` role, service `users.read`/
  `users.manage`/`roles.manage`): list/stats/detail/roles/sessions, create/update, activate/
  deactivate/restore/lock/unlock, admin-initiated reset-password and confirm-email, disable-2FA,
  revoke-tokens, delete.
- **Roles** (`/api/v1/roles/*`, gateway and service both `[Authorize(Roles="Admin")]`): CRUD plus
  membership add/remove. Its two list endpoints return a `PagedResult` (`pageNumber`/`pageSize`,
  default 50, at most 100).
- **Internal**: `GET /api/v1/users/{userId}/contact` is not routed through the gateway; it is
  called service-to-service with the `InternalService` API key.

Gateway policy controls determine external accessibility for protected paths.

Admin-reachable commands are recorded in this service's `audit_log` and served on `GET /api/v1/admin/audit`
(`audit.read`); see [Admin Audit Trail](../03-architecture/audit-log.md).

---

## Authorization Model

- JWT bearer authentication
- Role-based and policy-based access checks
- The permission model (`EShopPermissions`, 15 named permissions; `Admin` role bundles all of
  them) gates the admin-users and roles-management surface — see
  [Admin Audit Trail](../03-architecture/audit-log.md) and
  [frontend/conventions.md](../01-overview/frontend/conventions.md#5-permissions-and-admin-access)
- Additional internal API key checks for designated internal scenarios
- No token carries a `permission` claim. The login response and `GET /api/v1/account/profile`
  list the caller's `permissions`, derived from their roles through `RolePermissionBundles`

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

- [Frontend contracts: Identity](../01-overview/frontend/identity.md) — the authoritative endpoint reference
- [API Gateway](api-gateway.md)
- [Ordering Service](ordering-service.md)
- [Infrastructure - Security and Resilience](../06-infrastructure/resilience.md)

---

**Version**: 2.2  
**Last Updated**: 2026-09-26
