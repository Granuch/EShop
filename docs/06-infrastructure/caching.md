# Caching (Redis)

Redis is used as a core infrastructure component for basket state, distributed cache scenarios, and protection flows.

---

## Overview

Current Redis usage includes:
- Basket state storage
- Distributed cache support in selected services
- Token/protection-related cache scenarios (service-specific)
- Runtime resilience through cache wrapper patterns

---

## Runtime Setup

Redis runs from root `docker-compose.yml` using:
- `redis:7-alpine`
- password-protected startup configuration
- append-only persistence
- memory and eviction settings
- health checks

Typical local port mapping is controlled by `.env` (`REDIS_PORT`).

---

## Service Integration Pattern

Services use either:
- `IDistributedCache` (`AddStackExchangeRedisCache`), or
- `IConnectionMultiplexer` for advanced scenarios

Common behavior:
- Redis used when configured and reachable
- testing environment may use in-memory alternatives
- non-local environments apply stricter configuration validation

---

## Key Caching Patterns

### Cache-aside

1. Attempt read from cache.
2. On miss, load from source.
3. Store result with expiration.

### Invalidation on write

After data-changing operations, related keys are invalidated or refreshed.

### Circuit-breaking cache wrapper

Some services wrap cache calls with resilience behavior to prevent cascading failures when Redis is unstable.

---

## Operational Guidelines

- Use clear key namespaces per service.
- Keep TTLs aligned with data freshness needs.
- Avoid caching sensitive data unless explicitly required and controlled.
- Monitor hit/miss and latency trends through telemetry.

---

## Local Development Notes

- Local `.env` may include convenient password values.
- Replace local defaults with secure secret sources in non-local environments.

---

## Related Documents

- [Databases](databases.md)
- [Message Broker](message-broker.md)
- [Resilience](resilience.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
