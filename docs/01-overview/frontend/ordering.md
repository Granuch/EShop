# Ordering API

A customer's orders: reading them, changing items and the shipping address before payment, and cancelling. Plus the
admin order list, statistics, notes, status history, shipping and delivery.

> **Outline only.** The endpoint contracts are written in stage S6. Until then, use the service's OpenAPI document and
> [conventions.md](conventions.md).

**Verified at:** not yet verified.

## Base paths through the gateway

| Path | Methods | Gateway policy | Audience |
|---|---|---|---|
| `/api/v1/orders/**` | all | signed in | Storefront and admin panel (the service decides) |
| `/api/v1/users/{userId}/orders/**` | GET | signed in | Storefront |

Not routed through the gateway: Ordering's own `GET /api/v1/admin/settings` and `GET /api/v1/admin/audit` (the gateway
serves merged versions; see [admin-platform.md](admin-platform.md)).

## Storefront / customer

_Written in S6._

## Admin panel

_Written in S6._

## Types

_Written in S6._

## Frontend notes

_Written in S6._

---

**Version**: 0.1  
**Last Updated**: 2026-09-23
