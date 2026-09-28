# API Gateway

Single entry point for backend API traffic, implemented with ASP.NET Core + YARP.

---

## Overview

The API Gateway is responsible for:
- Reverse proxy routing to downstream services
- JWT authentication and authorization policy enforcement
- Global rate limiting, partitioned per client address. There is **no per-route limiting at the
  gateway** today: a `simulation` limiter policy is declared but attached to no route, and the
  named per-endpoint limits (Identity `auth`/`login`, Catalog `search`/`bulk`) all live in the
  services, not here — see [Rate Limiting](#rate-limiting) below
- CORS policy enforcement
- Correlation and request logging middleware
- Optional simulation middleware for controlled failure/latency scenarios
- Email trigger pipeline for selected operational events
- Health and metrics endpoints

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | Gateway host |
| Proxy | YARP | Route + cluster forwarding |
| Auth | JWT Bearer | Token validation |
| Authorization | Policy-based | Protected route access |
| Rate limiting | ASP.NET Core Rate Limiter | Traffic shaping |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

---

## Route and Cluster Model

Gateway routes map API path patterns to service clusters defined in configuration.

Common routed areas include:
- `/api/v1/auth/*` -> identity (anonymous)
- `/api/v1/account/*` -> identity (`Authenticated`)
- `/api/v1/admin/users/*` -> identity (`AdminArea`)
- `/api/v1/roles/*` -> identity (`AdminArea`)
- `/api/v1/products/*` and `/api/v1/categories/*` -> catalog (writes `AdminArea`, reads anonymous — except
  `GET /api/v1/products/deleted`, `GET /api/v1/products/export` and `GET /api/v1/categories/{id}/stats`, which have
  their own `AdminArea` routes at `Order: 19` so they win over the anonymous read routes at 21). The product import alone gets a larger request-body cap than the
  rest of catalog (`CatalogProxy:ImportMaxRequestBodySizeBytes`, 8 MiB, against the general 1 MiB) because its largest
  legal request is several megabytes; the bulk actions fit the general cap and keep it.
- `/api/v1/admin/catalog/*` -> catalog (`AdminArea`)
- `/api/v1/basket/admin/*` -> basket (`AdminArea`; wins over the next route because its `Order` is lower)
- `/api/v1/basket/*` -> basket
- Ordering's admin paths -> ordering (`AdminArea`, one route each at `Order: 39`, ahead of the storefront route at
  40): `GET /api/v1/orders` (the list; `POST` to the same path is a customer creating an order), `/api/v1/orders/stats`,
  and `/api/v1/orders/{id}/notes`, `/history`, `/ship`, `/deliver`
- `/api/v1/orders/*` -> ordering (`Authenticated`; the storefront)
- `/api/v1/users/{userId}/orders` (GET/HEAD/OPTIONS only) -> ordering (`Authenticated`; the
  service checks same-user-or-admin)
- Payment's admin paths -> payment (`AdminArea`, one route each at `Order: 39`): the bare `/api/v1/payments` (GET
  lists, POST settles), `/offline`, `/stats`, `/export`, `/simulation`, `/webhooks/**`, and `/{id}/events`, `/{id}/refund`
- `/api/v1/payments/*` -> payment (`Authenticated`; the storefront: `/create-intent` and `/{id}`). No proxy guard
  covers this prefix (a known, recorded hole — see the Middleware Pipeline section)
- `/api/v1/users/{userId}/payments` -> payment (`Authenticated`; same-user-or-admin, service-checked)
- `/api/v1/notifications/*` -> notification (`AdminArea`)
- `/api/v1/admin/audit` -> **served by the gateway itself** (`audit.read`): it asks all five audited services for a
  page and merges them. Not a YARP route, so it is absent from the routing-table test and pinned by
  `AuditLog/AuditLogFanOutTests` instead. See [Admin Audit Trail](../03-architecture/audit-log.md).
- `/api/v1/admin/cache/*` -> catalog (`AdminArea` here, `system.manage` in Catalog). The cache lever of the System page (admin
  panel S19); Catalog is the only service with service-wide cache families. `CatalogProxyGuardMiddleware` covers the prefix.
- `/api/v1/admin/health`, `/api/v1/admin/settings`, `/api/v1/admin/feature-flags` -> **served by the gateway itself**
  (`system.manage`), the rest of the System page:
  - **health** reads every service's anonymous `/health`, plus the gateway's own checks except `downstream` (which would
    probe every service a second time), and answers 200 with the worst status in the body. A service that cannot be
    reached is listed as unreachable and Unhealthy rather than failing the page. Only names and statuses are repeated —
    the same SEC-07 line as each service's own body.
  - **settings** asks Ordering (pricing currency; tax and shipping are reported as not applied, because none is
    modelled) and Payment (Stripe or the simulator) with the caller's token.
  - **feature-flags** reports the gateway's `Simulation` section as the middleware applies it, and asks Payment for its
    simulator values and webhook-signature bypass.

  Settings and flags are **read-only** — changing one is a redeploy (decision Q9c, applied to flags at S19). A service
  that cannot be read is named in `unavailableServices`. These are endpoints, not routes, so
  `SystemAdmin/SystemEndpointsTests` pins their policy, and also pins `SystemFanOut.Sources` against the real cluster list
  in both directions.

Authorization is applied per route where required (`Authenticated` or `AdminArea`).

The gateway declares **no `FallbackPolicy`**, so a route added without an `AuthorizationPolicy` is
anonymous and nothing fails. `Routes/GatewayRouteAuthorizationTests` pins the whole table for that
reason: adding a route without listing its policy fails the build.

Gateway authorization is defence in depth, not the enforcement point — every service re-checks the
caller. On admin routes the gateway asks only **"does the caller hold any permission?"**
(`AdminArea`, by a `permission` claim or a role bundle) and the service asks the exact question:
`/api/v1/notifications/*` is `AdminArea` here and `notifications.read` (the journal) or
`notifications.manage` (the operator actions) in Notification, and both must pass. Until
2026-09-27 the gateway asked for the `Admin` role instead, which would have refused an operator
whose role bundles only some permissions (frontend-contracts F-08); there is no `Admin` policy in
the gateway any more.

An admin endpoint under a storefront prefix needs its own `AdminArea` route with a lower `Order`,
or it is proxied to any caller the storefront route admits. Each service's integration suite
checks its admin endpoints against this file (`EveryAdminEndpoint_IsBehindTheGatewaysAdminGate`,
via the shared `tests/Shared/GatewayAdminGate.cs`), so a missing route fails the build.

---

## Security and Access Control

### JWT Validation

Gateway requires valid JWT configuration (`SecretKey`, `Issuer`, `Audience`) and enforces token validation before forwarding protected requests.

### Authorization Policies

Gateway defines route policies such as:
- `Authenticated`
- `AdminArea` — holds at least one EShop permission (`AdminAreaRequirement`, registered by
  `AddEShopPermissions()`)
- The gateway's own three endpoints (`/api/v1/admin/audit`, `/api/v1/admin/health`,
  `/api/v1/admin/settings`, `/api/v1/admin/feature-flags`) use the permission policies
  `audit.read` and `system.manage` directly — see
  [Route and Cluster Model](#route-and-cluster-model) above and
  [frontend/conventions.md](../01-overview/frontend/conventions.md#5-permissions-and-admin-access)
  for the full 15-permission model these compose with

### Operational Notices

The gateway emails operational notices (downstream failures, rate limiting, simulated failures, and successful writes
under `Gateway:CriticalSuccessPathPrefixes`) to the operators listed in `Gateway:OperationsEmailRecipients`, and to no
one else. The list is empty in the tracked configuration, so nothing is sent until an operator is configured; compose
sets it from `GATEWAY_OPERATIONS_EMAIL` (default `ops@eshop.local`, captured by Mailpit). The gateway no longer
resolves user email addresses through Identity and holds no internal API key.

---

## Rate Limiting

Gateway applies:
- Global limiter by partition (remote address)
- Optional dedicated limiter for simulation traffic

Behavior is configured through `RateLimiting` settings in gateway configuration.

Per-route throttling lives in the services, not here. Catalog's bulk actions, import and export (admin panel S16) carry
Catalog's own `bulk` policy, partitioned per client — so the limit holds for a caller that reaches the service directly
as well.

---

## Middleware Pipeline (High Level)

1. Global exception handling
2. Forwarded headers (when configured)
3. Request logging
4. Correlation middleware
5. Email trigger middleware
6. CORS
7. Rate limiter
8. HTTPS redirection (environment-dependent)
9. Authentication + Authorization
10. Proxy guard middlewares (identity, catalog, ordering, basket, notification — each matching a
    hardcoded path-prefix array; a route on a path none of them covers silently loses its
    request-body cap and leaks bare 502s, which `Routes/ProxyGuardCoverageTests` refuses.
    `/api/v1/payments` is a known, recorded hole)
11. Simulation decision/response middlewares
12. Reverse proxy forwarding

---

## Health and Metrics

Gateway exposes:
- `/health` — every check: `downstream` (each cluster's destinations), `smtp` (operator-notice mail server),
  `email-queue` and `gateway-liveness`
- `/health/ready` — only `email-queue`, i.e. whether this instance can serve. A stopped service or mail server does
  **not** make the gateway unready: its routes answer 502/503 and `/health` shows it, so an orchestrator routing on
  readiness keeps the gateway in rotation for every other route
- `/health/live` — `gateway-liveness`
- `/prometheus` (custom metrics)
- `/metrics` (OpenTelemetry metrics endpoint)

---

## Operational Notes

- Keep gateway route config synchronized with downstream service contracts.
- Keep non-local JWT and internal API key values non-placeholder.
- Use gateway logs/traces/metrics as first entrypoint for cross-service diagnostics.

---

## Related Documents

- [Frontend contracts: conventions](../01-overview/frontend/conventions.md) and
  [endpoint index](../01-overview/frontend/endpoint-index.md) — the authoritative cross-service
  reference (all 141 endpoints, both auth layers)
- [Gateway Runtime Guide](api-gateway-runtime-guide.md)
- [Identity Service](identity-service.md)
- [Catalog Service](catalog-service.md)
- [Infrastructure - Observability](../06-infrastructure/observability.md)

---

**Version**: 2.2  
**Last Updated**: 2026-09-26
