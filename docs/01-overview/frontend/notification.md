# Notification API

The admin panel's view of outgoing email: the delivery journal, statistics, templates and test sends, plus the
operator actions retry, resend and mark-undeliverable. Customers never call this service; their emails are sent from
events.

> **Outline only.** The endpoint contracts are written in stage S8. Until then, use the service's OpenAPI document and
> [conventions.md](conventions.md).

**Verified at:** not yet verified.

## Base paths through the gateway

| Path | Gateway policy | Audience |
|---|---|---|
| `/api/v1/notifications/**` | `Admin` role | Admin panel |

Not routed through the gateway: Notification's own `GET /api/v1/admin/audit` (see [admin-platform.md](admin-platform.md)).

## Admin panel

_Written in S8._

## Types

_Written in S8._

## Frontend notes

_Written in S8._

---

**Version**: 0.1  
**Last Updated**: 2026-09-23
