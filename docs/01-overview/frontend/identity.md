# Identity API

Accounts, sign-in, tokens, two-factor authentication and the user's own profile, plus the admin user and role
management screens.

> **Outline only.** The endpoint contracts are written in stage S2. Until then, use the service's OpenAPI document and
> [conventions.md](conventions.md). Authentication in general is described in
> [conventions.md §4](conventions.md#4-authentication).

**Verified at:** not yet verified.

## Base paths through the gateway

| Path | Gateway policy | Audience |
|---|---|---|
| `/api/v1/auth/**` | anonymous | Storefront |
| `/api/v1/account/**` | signed in | Storefront |
| `/api/v1/admin/users/**` | `Admin` role | Admin panel |
| `/api/v1/roles/**` | `Admin` role | Admin panel |

Not routed through the gateway: `GET /api/v1/users/{userId}/contact` (internal, service API key) and Identity's own
`GET /api/v1/admin/audit` (the gateway serves the merged trail; see [admin-platform.md](admin-platform.md)).

## Storefront / customer

_Written in S2._

## Admin panel

_Written in S2._

## Types

_Written in S2._

## Frontend notes

_Written in S2._

---

**Version**: 0.1  
**Last Updated**: 2026-09-23
