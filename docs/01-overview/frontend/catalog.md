# Catalog API

Products and categories as the storefront reads them, plus product and category administration: images, attributes,
discounts, stock, the recycle bin, bulk actions, CSV import and export, low-stock reporting and cache invalidation.

> **Outline only.** The core product and category contracts are written in stage S3, and the admin extras (bulk,
> import/export, low stock, category stats, cache) in S4. Until then, use the service's OpenAPI document and
> [conventions.md](conventions.md).

**Verified at:** not yet verified.

## Base paths through the gateway

| Path | Methods | Gateway policy | Audience |
|---|---|---|---|
| `/api/v1/products/**`, `/api/v1/categories/**` | GET, HEAD, OPTIONS | anonymous | Storefront (admins also see drafts) |
| `/api/v1/products/**`, `/api/v1/categories/**` | POST, PUT, PATCH, DELETE | `Admin` role | Admin panel |
| `/api/v1/products/deleted`, `/api/v1/products/export` | GET | `Admin` role | Admin panel |
| `/api/v1/admin/catalog/**` | all | `Admin` role | Admin panel |
| `/api/v1/admin/cache/**` | all | `Admin` role | Admin panel |

Not routed through the gateway: Catalog's own `GET /api/v1/admin/audit` (see [admin-platform.md](admin-platform.md)).

## Storefront / customer

_Written in S3._

## Admin panel

_Written in S3 (products and categories) and S4 (bulk, import/export, low stock, category stats, cache)._

## Types

_Written in S3 and S4._

## Frontend notes

_Written in S3 and S4._

---

**Version**: 0.1  
**Last Updated**: 2026-09-23
