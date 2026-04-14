# Resilience

Resilience controls reduce failure propagation across distributed services.

---

## Overview

Current resilience posture includes:
- Retry behavior for transient failures
- Circuit-breaker behavior for unstable dependencies
- Timeout and startup guard strategies
- Health/readiness/liveness endpoints
- Gateway-level rate limiting and policy enforcement
- Environment-aware fail-fast configuration validation

---

## Where Resilience Is Applied

### API Gateway

- Global rate limiting
- Route policy enforcement
- Middleware guards and controlled failure handling

### Service Runtime

- Dependency startup checks (JWT, DB, Redis, SMTP, etc.)
- Retry loops for startup database availability in selected services
- Circuit-breaking cache wrappers in cache-dependent services

### Messaging Layer

- MassTransit retry and circuit-breaker capabilities
- Consumer-level reliability controls

---

## Core Patterns

### Retry

Used for transient conditions where immediate failure is likely temporary.

### Circuit Breaker

Used to prevent repeated calls to unhealthy dependencies and reduce cascading pressure.

### Timeout and Fail Fast

Critical configuration and dependency conditions are validated early to avoid unsafe runtime behavior.

### Health Probes

Services expose readiness/liveness style endpoints to support orchestration and diagnostics.

---

## Operational Guidance

- Tune retry counts and intervals based on observed error profiles.
- Keep circuit-breaker thresholds realistic for service traffic shape.
- Avoid masking systemic issues with excessive retries.
- Track resilience events in logs and metrics for tuning decisions.

---

## Security and Environment Posture

- Non-local environments should reject placeholder secrets/config values.
- Local development can use convenient settings for faster onboarding.
- Production-like deployments require stricter auth/TLS/secret handling.

---

## Related Documents

- [Message Broker](message-broker.md)
- [Observability](observability.md)
- [API Gateway](../05-services/api-gateway.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
