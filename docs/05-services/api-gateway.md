# API Gateway

Single entry point for backend API traffic, implemented with ASP.NET Core + YARP.

---

## Overview

The API Gateway is responsible for:
- Reverse proxy routing to downstream services
- JWT authentication and authorization policy enforcement
- Global and route-specific rate limiting
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
- `/api/v1/auth/*` -> identity
- `/api/v1/admin/users/*` -> identity (`Admin`)
- `/api/v1/products/*` and `/api/v1/categories/*` -> catalog (writes `Admin`, reads anonymous — except
  `GET /api/v1/products/deleted` and `GET /api/v1/products/export`, which have their own `Admin` routes at `Order: 19`
  so they win over the anonymous read route at 21). The product import alone gets a larger request-body cap than the
  rest of catalog (`CatalogProxy:ImportMaxRequestBodySizeBytes`, 8 MiB, against the general 1 MiB) because its largest
  legal request is several megabytes; the bulk actions fit the general cap and keep it.
- `/api/v1/admin/catalog/*` -> catalog (`Admin`)
- `/api/v1/basket/admin/*` -> basket (`Admin`; wins over the next route because its `Order` is lower)
- `/api/v1/basket/*` -> basket
- `/api/v1/orders/*` -> ordering
- `/api/v1/payments/*` -> payment
- `/api/v1/notifications/*` -> notification (`Admin`)
- `/api/v1/admin/audit` -> **served by the gateway itself** (`audit.read`): it asks all five audited services for a
  page and merges them. Not a YARP route, so it is absent from the routing-table test and pinned by
  `AuditLog/AuditLogFanOutTests` instead. See [Admin Audit Trail](../03-architecture/audit-log.md).
- `/api/v1/admin/cache/*` -> catalog (`Admin` here, `system.manage` in Catalog). The cache lever of the System page (admin
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

Authorization is applied per route where required (for example `Authenticated`, `Admin`).

The gateway declares **no `FallbackPolicy`**, so a route added without an `AuthorizationPolicy` is
anonymous and nothing fails. `Routes/GatewayRouteAuthorizationTests` pins the whole table for that
reason: adding a route without listing its policy fails the build.

Gateway authorization is defence in depth, not the enforcement point — every service re-checks the
caller. Where the two differ deliberately, the gateway asks a *role* question and the service asks
a *permission* question: `/api/v1/notifications/*` is `Admin` here and `notifications.read` (the
journal) or `notifications.manage` (the operator actions) in Notification, and both must pass.
`/api/v1/basket/admin/*` is the same: `Admin` here, `baskets.read` or `system.manage` in Basket.

---

## Security and Access Control

### JWT Validation

Gateway requires valid JWT configuration (`SecretKey`, `Issuer`, `Audience`) and enforces token validation before forwarding protected requests.

### Authorization Policies

Gateway defines route policies such as:
- `Authenticated`
- `Admin`

### Internal Service Resolution

Gateway can resolve account email information through Identity service with internal API key/header configuration.

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
- `/health`
- `/health/ready`
- `/health/live`
- `/prometheus` (custom metrics)
- `/metrics` (OpenTelemetry metrics endpoint)

---

## Operational Notes

- Keep gateway route config synchronized with downstream service contracts.
- Keep non-local JWT and internal API key values non-placeholder.
- Use gateway logs/traces/metrics as first entrypoint for cross-service diagnostics.

---

## Related Documents

- [Gateway Runtime Guide](api-gateway-runtime-guide.md)
- [Identity Service](identity-service.md)
- [Catalog Service](catalog-service.md)
- [Infrastructure - Observability](../06-infrastructure/observability.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
