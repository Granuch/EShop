# Security Architecture

This document describes the current security architecture for the EShop backend platform.

---

## Security Principles

### 1) Defense in Depth

Security controls are applied across multiple layers:

1. **Environment and network layer**
   - Container/network isolation in local runtime
   - Controlled port exposure through compose settings

2. **Gateway layer**
   - JWT authentication and route authorization policies
   - Rate limiting controls
   - Request pipeline guards and middleware checks

3. **Service layer**
   - Service-level JWT validation where required
   - Input validation and business-rule enforcement

4. **Data and secrets layer**
   - Service-owned databases
   - Environment-based secret/config strategy

---

## Authentication and Authorization

### JWT-Based Authentication

Current stack uses JWT validation with issuer/audience/signing key checks.

Typical flow:
1. Client authenticates via identity endpoints.
2. Token is issued.
3. Client calls gateway with bearer token.
4. Gateway applies policy and routes to downstream service.
5. Downstream service processes request under authenticated context.

### Authorization Policies

The gateway enforces route-level authorization policies (for example authenticated and admin-only paths), reducing unauthorized access surface before requests reach downstream services.

### Permission Model

See [ADR-008](architecture-decisions.md#adr-008-layer-a-fine-grained-permission-model-under-role-based-authorization)
for the decision record (context, alternatives considered, and the risks accepted on adoption).

Underneath the gateway's role checks sits a fine-grained permission layer,
`EShopPermissions` (`BuildingBlocks/…/Authorization/`): 15 named permissions
(`users.read`, `users.manage`, `payments.write`, `system.manage`, `audit.read`, …), where the
permission string **is** the policy name. `RolePermissionBundles` maps the `Admin` role to all 15,
so a caller is granted a permission by either an explicit `permission` claim or an `Admin` role
claim — existing role-based tokens keep working with no re-issue needed. `AddEShopPermissions()` is
called in all seven components (gateway + six services).

Two layers apply independently and both must pass:
- **Gateway layer**: most admin routes require the `Admin` role at the route level
  (`AuthorizationPolicy: Admin` in the YARP route table). This is a coarse, role-only gate — the
  gateway does not evaluate permissions.
- **Service layer**: the service re-checks, either a specific permission (Identity's admin-users
  endpoints: `users.read`/`users.manage`/`roles.manage`; Payment's read/write endpoints:
  `payments.read`/`payments.write`; Notification's: `notifications.read`/`notifications.manage`;
  Catalog's/Ordering's system endpoints: `system.manage`; every service's own audit endpoint:
  `audit.read`) or, on endpoints that predate the permission model and have not been migrated, the
  `Admin` role directly (Payment's refund, settle and simulation endpoints; two of Basket's outbox
  endpoints).

**Known gaps, not yet fixed (docs-only findings, tracked for a decision):**
- **No gateway-level `Admin` gate on Ordering's or Payment's entire admin surface.** Both route
  through one catch-all per-service route with policy `Authenticated`; the admin/non-admin
  decision is made entirely by the service. No live bypass has been found (every admin endpoint
  of both services correctly answers 401/403 whether the gateway or the service is hit directly),
  but the redundant gateway-level check every other admin surface in this platform gets is absent
  here. Catalog's `GET /categories/{id}/stats` has the same single-endpoint gap.
- **Permissions are reported, not carried.** Access tokens carry no `permission` claim. The login
  response's `user.permissions` and `GET /api/v1/account/profile`'s `permissions` list what the
  caller's roles grant, computed from the same `RolePermissionBundles` table the services
  authorize against, so the UI and the services cannot disagree about the bundle. They report the
  roles held *now*, which after a role change can differ from the roles in a token already issued.
- **A role change and a role deletion are not retroactive on an already-issued token**, and a
  deleted role's claim can persist in a member's cached role list for up to 5 minutes after
  deletion.

See [`docs/01-overview/frontend/conventions.md`](../01-overview/frontend/conventions.md#5-permissions-and-admin-access)
for the full table of admin screens and the permission/role each needs, verified live against the
running stack.

---

## Internal Service Security

For sensitive internal interactions, services support API-key style internal authorization configuration via dedicated headers and settings.

This helps restrict internal-only endpoints and service-to-service sensitive operations.

---

## Input Validation and Request Safety

### Validation Pipeline

Validation is handled centrally through application pipeline behavior (FluentValidation + MediatR behavior).

### Data Access Safety

EF Core-based access reduces risk of unsafe query construction when used through normal query APIs.

### API Boundary Controls

Gateway and service middlewares enforce:
- request-size and policy checks
- structured error handling
- correlation and audit-friendly telemetry

---

## Rate Limiting and Abuse Protection

Rate limiting is applied **twice**: once at the gateway (a global partitioned limiter, keyed on
the client's forwarded IP address) and again inside each service (its own global limiter, plus
named policies on specific hot paths). Both layers are per-client-IP partitioned in every
deployment that sets `ForwardedHeaders:KnownNetworks`/`KnownProxies` correctly (compose and k8s
do); without that configuration a limiter degrades to one shared bucket for every caller behind
the gateway.

Representative limits (see each service's own `CLAUDE.md` and
[`frontend/conventions.md#7-rate-limits`](../01-overview/frontend/conventions.md#7-rate-limits)
for the current, code-verified numbers):
- Global default: 100 requests / 60 s per client IP, enforced at the gateway and independently in
  each service.
- Identity: `auth` (register/refresh/revoke/confirm) 10/min; `login` (login/forgot/reset) 5/min —
  plus a separate per-account login-throttle mechanism (`LoginAttemptTracker`) that is not a rate
  limiter and is not reset by it.
- Catalog: `search` (product list/newest) 30/min; `bulk` (bulk actions, import, export) 10/min.

Every component answers a rejected request the same way, through the shared
`EShopRateLimiting.UseEShopRejectionResponse()`: a problem+json 429 with `errorCode`
`Request.RateLimited` and a `Retry-After` header in whole seconds.

This applies at ingress level before downstream service execution, and again inside each service
as a second line of defense against a caller that reaches it directly.

---

## Secret and Configuration Strategy

### Local Development

- Convenient local password-based values in `.env` and local config are allowed.
- This supports fast onboarding and local reproducibility.

### Non-Local Environments

- Placeholder values must be replaced.
- Startup validation and configuration checks are used to fail fast on unsafe settings.
- Real secrets must not be committed to source control.

---

## Transport and Runtime Hardening

Current runtime includes environment-aware HTTPS redirection behavior and forwarded-header handling for reverse-proxy scenarios.

Additional hardening expectations for higher environments:
- TLS termination with trusted certificates
- restricted ingress
- secret store integration
- image and dependency vulnerability scanning

---

## Observability and Security Monitoring

Security-relevant diagnostics are supported through:
- Structured logs (Serilog + Seq)
- Request correlation IDs
- Health/readiness probes
- Metrics and traces (Prometheus + OpenTelemetry + Jaeger)

This enables detection and investigation of auth failures, policy rejections, and runtime anomalies.

### Diagnostic endpoint exposure

All three health endpoints (`/health`, `/health/ready`, `/health/live`) are anonymous in every
component, because a probe cannot present a credential. To keep that safe, their response bodies
carry **only the overall status, the total duration, and a name/status pair per check** — no
check description, no data dictionary, and no exception message. The shared
`EShopHealthResponseWriter` enforces this, and the checks themselves also refrain from attaching
exceptions to their results; the failure detail is written to the server log instead. This
replaced `UIResponseWriter.WriteHealthCheckUIResponse`, which serialised all of it and therefore
returned database hostnames, usernames and raw connection errors to any caller, precisely when a
service was failing.

The Prometheus scrape endpoints (`/prometheus`, `/metrics`) are likewise anonymous but are
restricted by network. With nothing configured they answer only loopback and private ranges
(RFC 1918 and IPv6 unique-local), which covers the compose bridge network, a Kubernetes pod CIDR
and localhost — every scrape path this platform actually uses. `Metrics:AllowedNetworks` (a list
of CIDRs) replaces that default when a deployment needs something narrower or wider. A rejected
request receives 404 rather than 403, so the response does not confirm the endpoint exists. The
`Testing` environment is exempt in full, because `TestServer` has no socket and therefore no
remote address to evaluate.

### Accepted risk: OpenAPI in production

All seven components (the gateway and all six services) serve their OpenAPI document and Scalar
UI in **every environment except Production**, through one shared rule, `EShopApiDocs.IsExposedIn`
(`BuildingBlocks`). This was unified from an earlier, inconsistent state where three services
exposed the schema in Production too and three gated it on `IsDevelopment()` only — that split no
longer exists; every component now agrees.

Exposing the full schema outside Production (including in a shared Sandbox/staging environment)
is a **known and deliberate choice, not an oversight**: it is recorded here so it is a stated
decision rather than something rediscovered during a review. Revisit it before any deployment that
is reachable from outside a trusted network — a published schema is reconnaissance material.

---

## Threat Focus Areas

Primary threat categories considered:
- Unauthorized API access
- Credential or secret leakage
- Abuse traffic and brute-force patterns
- Misconfiguration in non-development environments
- Cross-service trust boundary misuse

Mitigation is distributed across gateway policy, service validation, configuration checks, and monitoring.

---

## Security Checklist (Operational)

- JWT signing key is strong and non-placeholder.
- Issuer/audience settings are correct.
- Internal API key settings are set for non-local deployments.
- Exposed ports are intentional.
- Health endpoints are monitored.
- Logs/metrics/traces are available for incident analysis.

---

## Related Documents

- [Architecture Decisions](architecture-decisions.md)
- [Data Flow](data-flow.md)
- [Infrastructure Security and Resilience](../06-infrastructure/)
- [Frontend API Contracts — Conventions](../01-overview/frontend/conventions.md)

---

**Version**: 2.1  
**Last Updated**: 2026-09-26
