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

After data-changing operations, related keys are invalidated or refreshed. `CacheInvalidationBehavior`
(a MediatR pipeline behavior — see [Design Patterns](../03-architecture/design-patterns.md)) can
only evict entries it can name, and it names them two ways:
- **Exact keys** (`ICacheInvalidatingCommand.CacheKeysToInvalidate`): works only when the writer
  builds the identical key the reader used (same prefix, same version segment). It cannot express
  "evict every page of this list," so a key that embeds request parameters (a filtered/paged
  query) needs the family mechanism below instead — an exact-key eviction for such a key silently
  removes nothing while still logging success.
- **Versioned families** (`CacheFamiliesToInvalidate` / `IVersionedCacheKey`): a write bumps a
  version counter through `ICacheKeyVersionProvider`, and every reader's cache key embeds the
  current version, so bumping the family invalidates every entry under it at once without naming
  each one. Only services that register a version provider support this (currently Catalog —
  `products:list`, `categories:list` — and Ordering, per-user); a family declared without a
  registered provider is a no-op that logs a warning, the same silent-failure shape as an
  unmatched exact key or a wildcard pattern (`IDistributedCache` has no SCAN, so a `*` key is
  logged and skipped, never evicted).

**Manual invalidation lever.** Catalog exposes `POST /api/v1/admin/cache/invalidate`
(`system.manage`), which lets an operator bump one or more families on demand — useful when a
write path that does not automatically raise an eviction (for example an image or attribute
mutation, which raises no domain/integration event) has left a list stale. It answers 503
`Cache.Unavailable` rather than a false 200 if Redis itself refuses the bump. See
[`frontend/catalog.md`](../01-overview/frontend/catalog.md) for the endpoint's contract.

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
- [Design Patterns — Pipeline Behavior Pattern](../03-architecture/design-patterns.md)
- [Frontend API Contracts — Catalog](../01-overview/frontend/catalog.md)

---

**Version**: 2.1  
**Last Updated**: 2026-09-26
