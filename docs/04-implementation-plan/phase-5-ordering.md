# Phase 5: Ordering Service

**Suggested Duration**: 5-7 working days  
**Primary Owners**: Ordering contributors  
**Current Status**: Implemented and evolving

---

## Goal

Maintain reliable order lifecycle management and integration with basket, payment, and notification flows.

---

## Scope

1. Order creation and status transitions.
2. Consumer/producer event contracts for order workflow.
3. User and admin order query behavior.
4. Failure-handling paths for payment-dependent transitions.

---

## Current Baseline

Ordering service currently includes:
- Layered architecture and CQRS-style handlers
- PostgreSQL persistence
- MassTransit/RabbitMQ integration
- Health and telemetry support

---

## Work Items

### 1) Order lifecycle consistency
- Validate transition guards and business rules.
- Ensure cancellation and terminal states are consistent.

### 2) Event integration
- Validate consumed basket/payment events.
- Validate emitted order events for downstream services.

### 3) Query and access control
- Ensure users access only owned orders.
- Keep admin/all-orders behavior explicit and audited.

### 4) Quality and tests
- Expand tests for state transitions and event-driven paths.

---

## Deliverables

- Stable order lifecycle behavior.
- Verified event contract compatibility with dependent services.
- Updated tests and documentation.

---

## Exit Criteria

- Ordering service passes build/tests.
- Key event-driven flows succeed in integration testing.
- No unresolved critical ordering defects.

---

## Next Phase

- [Phase 6: Payment Service](phase-6-payment.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
