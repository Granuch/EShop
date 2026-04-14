# Phase 3: Catalog Service

**Suggested Duration**: 5-7 working days  
**Primary Owners**: Catalog contributors  
**Current Status**: Implemented and evolving

---

## Goal

Maintain robust product and category APIs with reliable read performance and clear admin boundaries.

---

## Scope

1. Product/category CRUD and query behavior.
2. Cache and persistence consistency.
3. Admin write protections through gateway policy and service checks.
4. Search/filter/pagination quality.

---

## Current Baseline

Catalog service already includes:
- Layered architecture and application handlers
- PostgreSQL persistence via infrastructure layer
- Redis distributed caching paths
- Messaging integration support
- OpenTelemetry + health endpoint support

---

## Work Items

### 1) API contract stabilization
- Validate product and category route behavior.
- Keep error responses and validation messages consistent.

### 2) Read path optimization
- Review cache-hit behavior and TTL strategy.
- Ensure fallback path to database is reliable.

### 3) Data integrity
- Verify write-side business rules and validation.
- Keep migrations and schema evolution safe.

### 4) Quality and tests
- Add/update tests for filtering, paging, cache behavior, and admin-only writes.

---

## Deliverables

- Stable catalog API contracts.
- Predictable read performance under normal load.
- Updated tests and docs for changed behavior.

---

## Exit Criteria

- Catalog service passes build/tests.
- Public read and admin write routes behave as documented.
- No unresolved critical catalog defects.

---

## Next Phase

- [Phase 4: Basket Service](phase-4-basket.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
