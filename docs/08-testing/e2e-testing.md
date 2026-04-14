# End-to-End Testing Guide

End-to-end workflow validation guidance for the backend-first repository.

---

## Scope in This Repository

This repository is backend-focused.

E2E here means critical business workflow validation across services (API-level end-to-end), for example:
- authentication flow
- catalog -> basket -> checkout flow
- order -> payment -> notification progression

External UI/browser E2E tests are optional and typically maintained in client repositories.

---

## Core Workflow Scenarios

Recommended minimum workflow checks:

1. User/auth flow
2. Product discovery/read flow
3. Basket lifecycle flow
4. Checkout/order creation flow
5. Payment outcome and order status update flow
6. Notification trigger flow

---

## Design Principles

- Validate full cross-service outcomes, not isolated unit behavior
- Keep scenarios few but high-value
- Prefer deterministic test data and cleanup logic
- Include both success and key failure scenarios

---

## Environment Requirements

- Complete service stack available (local or integration environment)
- Required infrastructure dependencies running
- Stable test credentials/configuration

---

## Validation Focus

For each scenario verify:
- expected API responses
- expected persisted state transitions
- expected emitted/consumed integration behavior (where observable)

---

## Failure Triage

When E2E fails:
1. check gateway/service health endpoints
2. inspect logs for correlation IDs
3. inspect traces across service boundaries
4. isolate contract/config regression vs transient environment issue

---

## Maintenance Rules

- Keep E2E suite small and reliable
- Remove obsolete scenarios when workflows change
- Update E2E docs when critical flow contracts change

---

## Related Documents

- [Testing Strategy](testing-strategy.md)
- [Integration Testing](integration-testing.md)
- [Services](../05-services/)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
