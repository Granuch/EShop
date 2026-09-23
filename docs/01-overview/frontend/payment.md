# Payment API

Card payments through Stripe (payment intents), a customer's payment history, and the admin payment screens: list,
statistics, CSV export, offline settlement, refunds, event timeline and webhook replay.

> **Outline only.** The endpoint contracts are written in stage S7. Until then, use the service's OpenAPI document and
> [conventions.md](conventions.md).

**Verified at:** not yet verified.

## Base paths through the gateway

| Path | Gateway policy | Audience |
|---|---|---|
| `/api/v1/payments/**` | signed in | Storefront and admin panel (the service decides) |
| `/api/v1/users/{userId}/payments` | signed in | Storefront |

Not routed through the gateway:
- `POST /webhooks/stripe`, which Stripe calls server to server;
- Payment's own `GET /api/v1/admin/settings`, `/api/v1/admin/feature-flags` and `/api/v1/admin/audit`. The gateway
  serves merged versions; see [admin-platform.md](admin-platform.md).

> ⚠ The gateway has no guard on Payment's paths: no 1 MiB body cap, and a bare 502 instead of a 503 when Payment is
> down. See [conventions.md §3.4](conventions.md#34-errorcode-values). (F-13)

## Storefront / customer

_Written in S7._

## Admin panel

_Written in S7._

## Types

_Written in S7._

## Frontend notes

_Written in S7._

---

**Version**: 0.1  
**Last Updated**: 2026-09-23
