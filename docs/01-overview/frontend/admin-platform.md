# Admin platform endpoints (served by the gateway)

Four admin endpoints are answered by the API gateway itself rather than proxied to one service. Each one fans out to
the services, with a 5 s limit per service, and merges the answers:

- the admin audit trail across every audited service;
- aggregate system health;
- the read-only settings view;
- the read-only feature flags.

> **Outline only.** The endpoint contracts are written in stage S8. Until then, use the gateway's OpenAPI document and
> [conventions.md](conventions.md).

**Verified at:** not yet verified.

## Endpoints

| Path | Gateway policy | Audience |
|---|---|---|
| `GET /api/v1/admin/audit` | `audit.read` permission | Admin panel |
| `GET /api/v1/admin/health` | `system.manage` permission | Admin panel |
| `GET /api/v1/admin/settings` | `system.manage` permission | Admin panel |
| `GET /api/v1/admin/feature-flags` | `system.manage` permission | Admin panel |

The services serve their own audit, settings and flags endpoints at the same paths, but those are not reachable
through the gateway.

## Admin panel

_Written in S8._

## Types

_Written in S8._

## Frontend notes

_Written in S8._

---

**Version**: 0.1  
**Last Updated**: 2026-09-23
