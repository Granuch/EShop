# API Gateway Runtime Guide

Detailed runtime guide for `src/ApiGateways/EShop.ApiGateway`.

---

## 1. Service Role

`EShop.ApiGateway` is the ingress service for backend APIs.

It combines:

- YARP reverse proxy
- JWT auth and route policy enforcement
- Rate limiting
- Simulation middleware behavior (configurable)
- Email-trigger pipeline for selected operational outcomes
- Health and telemetry endpoints

---

## 2. Main Composition Points

Primary runtime setup is in `Program.cs`:

1. Options binding from configuration sections
2. Serilog host setup
3. JWT auth + authorization policies
4. CORS and rate limiter
5. YARP route/cluster loading from `ReverseProxy`
6. OpenTelemetry + metrics
7. Email queue/dispatcher services
8. Health check registration

---

## 3. Request Pipeline Order (Current)

At a high level:

1. Global exception handler
2. Forwarded headers (when known proxies are configured)
3. Request logging
4. Correlation middleware
5. Email trigger middleware
6. CORS
7. Rate limiter
8. HTTPS redirection (if configured)
9. Authentication
10. Authorization
11. Proxy guard middlewares
12. Simulation middlewares
13. YARP reverse proxy mapping
14. Metrics + health endpoints

Pipeline order matters because email/simulation/logging behavior depends on where middleware executes.

---

## 4. Routing and Clusters

Routing is configured under `ReverseProxy` in gateway settings:

- Route definitions (`Routes`)
- Destination groups (`Clusters`)

Routes support:

- Path matching
- Method filtering
- Per-route authorization policy
- Service destination selection

Six clusters are declared: `identity-cluster`, `catalog-cluster`, `basket-cluster`,
`ordering-cluster`, `payment-cluster` and `notification-cluster`.

Two rules are easy to get wrong here:

- There is **no `FallbackPolicy`**, so a route with no `AuthorizationPolicy` is anonymous and
  nothing fails at startup. `Routes/GatewayRouteAuthorizationTests` pins every `/api/` route and
  its policy so a new one cannot ship without a decision.
- Every route must also sit behind one of the `*ProxyGuardMiddleware` path-prefix arrays
  (identity, catalog, ordering, basket, notification). A route outside them works perfectly while
  losing its request-body cap and the 502→503 rewrite; `Routes/ProxyGuardCoverageTests` is what
  catches that. `/api/v1/payments` is recorded there as a known pre-existing hole.
- Comments inside `Routes` only work under a route's `Metadata`: every other key is parsed as a
  route id and fails startup.

---

## 5. Simulation Layer

Simulation behavior is controlled by `Simulation` config:

- Global enable flag
- Per-route profiles
- Delay range
- Error rate
- Forced failure mode
- Optional header override support

This is useful for resilience testing and operational drills without changing downstream services.

---

## 6. Email Trigger Pipeline

Gateway can enqueue notification events based on response outcomes: downstream failures (5xx), rate limiting (429),
simulated failures, and successful **writes** under `Gateway:CriticalSuccessPathPrefixes` (reads never qualify).

Runtime components:

- Queue abstraction
- Background dispatcher
- Template engine
- SMTP sender

Notices go only to the operators in `Gateway:OperationsEmailRecipients`, never to the user whose request caused them.
With the list empty (the tracked default) nothing is queued. This allows asynchronous operational notifications
without blocking request flow.

---

## 7. Security Requirements

Minimum required security configuration:

- `JwtSettings:SecretKey` (>= 32 chars)
- `JwtSettings:Issuer`
- `JwtSettings:Audience`

Use local override files for local-only secrets/config and keep non-local values secure.

---

## 8. Rate Limiting

Current runtime supports:

- Global fixed-window limits
- Dedicated simulation limiter

Expected behavior on rejection: `429 Too Many Requests` with gateway-side observability signals.

---

## 9. Health and Telemetry

Health endpoints:

- `/health`
- `/health/ready`
- `/health/live`

Metrics endpoints:

- `/prometheus`
- `/metrics`

Telemetry stack:

- Serilog (logs)
- OpenTelemetry (traces + metrics)
- Prometheus scraping support

---

## 10. Troubleshooting Checklist

1. Validate gateway health endpoint status.
2. Confirm JWT settings are loaded and non-placeholder.
3. Verify route and cluster definitions map to reachable destinations.
4. Check rate-limiter settings for unexpected throttling.
5. Inspect logs with correlation IDs.
6. Use traces to follow end-to-end route behavior.

---

## Related Documents

- [API Gateway Overview](api-gateway.md)
- [Infrastructure - Observability](../06-infrastructure/observability.md)
- [Infrastructure - Resilience](../06-infrastructure/resilience.md)

---

**Version**: 2.1  
**Last Updated**: 2026-09-25
