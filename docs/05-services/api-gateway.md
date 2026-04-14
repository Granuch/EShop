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
- `/api/v1/products/*` and `/api/v1/categories/*` -> catalog
- `/api/v1/basket/*` -> basket
- `/api/v1/orders/*` -> ordering
- `/api/v1/payments/*` -> payment

Authorization is applied per route where required (for example `Authenticated`, `Admin`).

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
10. Proxy guard middlewares
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
