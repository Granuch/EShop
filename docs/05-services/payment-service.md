# Payment Service

Payment processing service with Stripe-oriented integration and event-driven workflow participation.

---

## Overview

Payment Service provides:
- Payment lifecycle handling
- Stripe integration paths and webhook-related runtime support
- Event-driven communication with ordering workflow
- Configuration and safety guards by environment
- Health and telemetry integration

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | API host |
| Database | PostgreSQL | Payment persistence |
| Messaging | RabbitMQ + MassTransit | Async workflow events |
| Payment integration | Stripe configuration/services | Payment processing |
| Security | JWT + policy handlers | Protected payment access |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

---

## Project Structure

Payment service follows layered architecture:

- `EShop.Payment.API`
- `EShop.Payment.Application`
- `EShop.Payment.Domain`
- `EShop.Payment.Infrastructure`

---

## Runtime Characteristics

### Environment Safety

Startup includes strict checks for:
- JWT configuration strength
- placeholder values in non-local environments
- unsafe Stripe webhook verification bypass outside allowed environments

### Database Initialization

Service applies migrations at startup with retry behavior for delayed database readiness.

### Authorization

Includes role and user-scoped policy support for protected operations.

---

## Workflow Role

Payment service participates in asynchronous order flow by:
- consuming relevant payment-triggering events
- processing payment outcomes
- publishing payment result events for ordering updates

---

## API Areas (High Level)

**Full endpoint-by-endpoint contracts (routes, both auth layers, request/response shapes, error
tables, TypeScript types) live in
[frontend/payment.md](../01-overview/frontend/payment.md), the authoritative source. This section
is a summary, not a duplicate.**

16 endpoints:
- **Storefront**: `create-intent` (returns a Stripe `clientSecret`; a repeat call for the same
  order **resumes** the existing intent rather than erroring, fixed after the frontend-contracts
  audit — see [frontend/payment.md](../01-overview/frontend/payment.md#post-apiv1paymentscreate-intent));
  get by id (another user's payment is 404, not 403); `GET /api/v1/users/{userId}/payments`.
  `status` and `paymentMethod` are sent as PascalCase names (`"Success"`, `"Stripe"`), like every
  service's enums. USD only.
- **Admin**: list, stats, CSV export (all `payments.read`), offline settle and failed-webhook
  replay (`payments.write`), `POST /payments` settle, refund and the simulation diagnostics
  (`Admin` role — one of two authorization styles Payment mixes, see
  [frontend/payment.md](../01-overview/frontend/payment.md#frontend-notes)), and the
  per-payment event timeline with its actor.
- **Not routed through the gateway**: `POST /webhooks/stripe` (Stripe's own signature, no JWT).

After Stripe confirms a payment, the order moves to `Paid` asynchronously through the webhook —
the client polls the order, not the payment.

Gateway and service policies control route protection. Like Ordering's, each Payment admin
endpoint has its own gateway route with the `AdminArea` policy, ahead of the storefront's
`/api/v1/payments/**` route, and Payment checks the exact permission or the `Admin` role behind it
(see [frontend/payment.md](../01-overview/frontend/payment.md#base-paths-through-the-gateway)).

Admin-reachable commands are recorded in this service's `audit_log` and served on `GET /api/v1/admin/audit`
(`audit.read`); see [Admin Audit Trail](../03-architecture/audit-log.md).

`GET /api/v1/admin/settings` and `GET /api/v1/admin/feature-flags` (`system.manage`, admin panel S19) are this service's
slices of the System page: the provider (`Stripe` when `Stripe:Enabled`, otherwise `Simulator`), and the simulator's
configured values plus the webhook-signature bypass. Both are read-only and carry no key. The gateway serves the composed
pages on the same paths and does not route them here. The older `GET /api/v1/payments/simulation` is unchanged.

---

## Health and Telemetry

Payment service exposes:
- `/health/ready`
- `/health/live`
- `/prometheus`
- `/metrics`

And emits structured logs, traces, and metrics for payment diagnostics.

---

## Operational Notes

- Keep Stripe and JWT secrets environment-specific and non-placeholder.
- Treat webhook and idempotency behavior as critical regression areas.
- Validate payment state transitions with integration tests.

---

## Related Documents

- [Frontend contracts: Payment](../01-overview/frontend/payment.md) — the authoritative endpoint reference
- [Ordering Service](ordering-service.md)
- [API Gateway](api-gateway.md)
- [Infrastructure - Message Broker](../06-infrastructure/message-broker.md)

---

**Version**: 2.1  
**Last Updated**: 2026-09-26
