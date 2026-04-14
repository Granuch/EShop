# Phase 4: Basket Service

**Suggested Duration**: 4-6 working days  
**Primary Owners**: Basket + integration contributors  
**Current Status**: Implemented and evolving

---

## Goal

Provide reliable basket lifecycle handling with Redis-backed state and safe checkout initiation.

---

## Scope

1. Basket CRUD operations and quantity updates.
2. Redis data model and expiration behavior.
3. Checkout trigger into asynchronous ordering flow.
4. Authenticated user basket behavior and merge scenarios.

---

## Current Baseline

Basket service currently includes:
- Dedicated API/Application/Domain/Infrastructure projects
- Redis-backed basket persistence
- Downstream integration through messaging and service calls
- Observability and health checks

---

## Work Items

### 1) Basket correctness
- Validate add/update/remove semantics.
- Ensure total calculations remain deterministic.

### 2) Checkout safety
- Validate required checkout payload and preconditions.
- Ensure checkout publishes expected integration message(s).

### 3) Redis reliability
- Confirm key naming and TTL policy consistency.
- Validate behavior under reconnect/restart scenarios.

### 4) Quality and tests
- Extend unit/integration tests for basket lifecycle and checkout edge cases.

---

## Deliverables

- Stable basket API behavior.
- Reliable checkout initiation path.
- Updated tests and documentation.

---

## Exit Criteria

- Basket service builds and passes tests.
- Redis-backed flows behave consistently.
- Checkout path is validated end-to-end with downstream consumers.

---

## Next Phase

- [Phase 5: Ordering Service](phase-5-ordering.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
