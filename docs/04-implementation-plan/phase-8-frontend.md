# Phase 8: Client Integration Track (External UI)

**Suggested Duration**: 5-8 working days  
**Primary Owners**: API consumers / client team  
**Current Status**: Optional track outside this repository

---

## Goal

Integrate external clients (web/mobile/tools) with the backend APIs through the gateway.

---

## Scope

This repository is backend-focused. Client application source code is not maintained under `src` in the current structure.

This phase documents integration expectations for external clients:
1. Authentication and token usage.
2. API consumption patterns via gateway routes.
3. Error handling and retry strategy in client applications.
4. UX behavior for asynchronous workflows (order/payment/notification delays).

---

## Integration Work Items

### 1) Contract alignment
- Confirm endpoint contracts with service docs.
- Validate request/response models and error formats.

### 2) Auth integration
- Implement sign-in/refresh/logout token handling.
- Enforce secure token storage approach for target platform.

### 3) Feature flows
- Product listing and detail reads.
- Basket management.
- Checkout and order status visibility.

### 4) Operational concerns
- Handle rate-limit and transient-failure responses gracefully.
- Surface correlation IDs in client diagnostics where possible.

---

## Deliverables

- External client can complete core flow: auth -> catalog -> basket -> checkout -> order view.
- Integration issues are documented and tracked back to API owners when needed.

---

## Exit Criteria

- Core end-to-end client scenario validated against current backend.
- Blocking integration issues resolved or explicitly documented.

---

## Next Phase

- [Phase 9: Testing](phase-9-testing.md)

---

**Version**: 2.0  
**Last Updated**: 2026-04-14
