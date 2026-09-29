# Phase 8: Client Integration Track (External UI)

**Suggested Duration**: 5-8 working days  
**Primary Owners**: API consumers / client team  
**Current Status**: `ui/` exists as an unintegrated scaffold; the contracts it would integrate
against are complete and code-verified (see below). The integration work itself has not started.

---

## Goal

Integrate `ui/` (or any other client — web/mobile/tools) with the backend APIs through the
gateway.

---

## Current State

`ui/` is a tracked Next.js 16 / React 19 / Tailwind 4 scaffold (shadcn, zustand; 35 files). It is
**not integrated**: there is no `fetch` call anywhere in it, no `NEXT_PUBLIC_*` configuration, no
token storage and no API client — its login form submits to `console.log("It works")`. There is
no test setup beyond `dev`/`build`/`start`/`lint`. `src/ClientApp/eshop-web` is a dead, untracked
directory left over from an earlier attempt and should not be used.

This repository remains backend-focused; the frontend's *application* code is not maintained
under `src`, and this phase's scope is the integration work, not building `ui/` out feature by
feature.

## Scope

**The contract side of this phase is done.** [`docs/01-overview/frontend/`](../01-overview/frontend/README.md)
is a frontend-ready, code-verified contract set covering all 141 application endpoints —
storefront and admin, both auth layers, error tables, paging, rate limits, TypeScript types, and
end-to-end flow diagrams with sequence diagrams for auth, checkout-to-payment, cancel/refund and
the admin workflows. It is treated as authoritative and expected to stay in sync with the code
(any endpoint/DTO change updates it — see the review checklist in
[07-development-workflow/code-review-process.md](../07-development-workflow/code-review-process.md)).
Known code quirks a client must accommodate are called out inline with a ⚠ and tracked with an
F-id for triage.

What remains is wiring `ui/` (or another client) against that contract set:
1. Authentication and token usage — see
   [`frontend/conventions.md#4-authentication`](../01-overview/frontend/conventions.md#4-authentication).
2. API consumption patterns via gateway routes — see
   [`frontend/endpoint-index.md`](../01-overview/frontend/endpoint-index.md) for the full route
   table and [`frontend/conventions.md#1-base-url-and-routing`](../01-overview/frontend/conventions.md#1-base-url-and-routing).
3. Error handling and retry strategy — see
   [`frontend/conventions.md#3-errors`](../01-overview/frontend/conventions.md#3-errors) for the
   three validation-error shapes and the recommended client algorithm.
4. UX behavior for asynchronous workflows (order/payment/notification delays) — see
   [`frontend/flows.md`](../01-overview/frontend/flows.md) for what to poll and how long to expect.

---

## Integration Work Items

### 1) Contract alignment
- Build the API client against [`docs/01-overview/frontend/`](../01-overview/frontend/README.md),
  not the old, non-authoritative `Data Contracts.md` (now a redirect stub).
- Validate request/response models and error formats against the TypeScript types already
  published in each service's contract file.

### 2) Auth integration
- Implement sign-in/refresh/logout token handling per `frontend/conventions.md`'s auth section,
  including the `requires2FA` branch and silent-refresh rotation.
- Enforce secure token storage approach for target platform (Next.js: see the storage
  recommendation in `conventions.md`).

### 3) Feature flows
- Product listing and detail reads.
- Basket management.
- Checkout and order status visibility, including the create-intent → Stripe.js → webhook → order
  `Paid` polling sequence documented in `frontend/flows.md`.

### 4) Operational concerns
- Handle rate-limit and transient-failure responses gracefully — every 429 is problem+json
  `Request.RateLimited` with a `Retry-After` header.
- Surface correlation IDs in client diagnostics where possible — the CORS policy exposes
  `X-Correlation-ID`, `Location`, `Retry-After` and `Content-Disposition` to browser JavaScript.

---

## Deliverables

- External client can complete core flow: auth -> catalog -> basket -> checkout -> order view.
- Integration issues are documented and tracked back to API owners when needed — the frontend
  contracts' `⚠` notes and their linked findings are the starting list.

---

## Exit Criteria

- Core end-to-end client scenario validated against current backend.
- Blocking integration issues resolved or explicitly documented.

---

## Next Phase

- [Phase 9: Testing](phase-9-testing.md)

---

**Version**: 3.0  
**Last Updated**: 2026-09-26
