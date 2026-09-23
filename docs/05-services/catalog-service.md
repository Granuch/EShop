# Catalog Service

Product and category service for read/write catalog operations.

---

## Overview

Catalog Service provides:
- Product and category API operations
- Public read and protected write paths
- Validation-driven command/query handling
- Redis-backed cache integration
- Messaging hooks for cross-service workflows
- Health and telemetry endpoints

---

## Technology

| Component | Technology | Purpose |
|-----------|------------|---------|
| Runtime | ASP.NET Core (.NET 10) | API host |
| Data | PostgreSQL | Catalog persistence |
| Cache | Redis | Distributed cache scenarios |
| Messaging | RabbitMQ + MassTransit | Async integration |
| Validation | FluentValidation + pipeline behavior | Request validation |
| Mapping | Mapster | DTO/object mapping |
| Observability | Serilog + OpenTelemetry + Prometheus | Logs, traces, metrics |

---

## Project Structure

Catalog service follows layered architecture:

- `EShop.Catalog.API`
- `EShop.Catalog.Application`
- `EShop.Catalog.Domain`
- `EShop.Catalog.Infrastructure`

---

## Runtime Characteristics

### Seed Data

On startup, after migrations, Catalog seeds a small baseline catalog via `CatalogSeedData`
(`EShop.Catalog.Infrastructure/Data/CatalogSeedData.cs`), gated to Development and Sandbox only.

- Creates a couple of categories (parent/child) and several products covering key edge
  cases: multiple images with explicit ordering, key/value attributes, an active discount,
  a product with no images, and one left in `Draft` status.
- Idempotent: skipped entirely if any category already exists.
- Goes through the same domain factory methods + `SaveChangesAsync` path as
  `CreateProductCommandHandler`, so seeded products raise `ProductCreatedEvent` → outbox →
  integration events, same as if created through the API (visible to downstream read
  models, e.g. Basket).

### Startup Validation

Catalog startup validates critical configuration in non-local environments, including database and JWT settings.

### Cache Strategy

Redis distributed cache is used when configured, with fallback behavior for testing/local fallback paths.

### Messaging

MassTransit integration supports publishing/consuming integration events for cross-service catalog interactions.

---

## API Areas (High Level)

Typical route groups include:
- Products (read and admin write operations)
- Categories (read and admin write operations)

Gateway enforces route-level authorization on protected write paths.

---

## Product Images and Attributes

Images are stored as **URLs only** — the service accepts no file uploads and owns no
object storage. An admin supplies an absolute `http`/`https` link (≤500 characters) that
some external host or CDN already serves.

| Concern | Rule |
|---|---|
| Max images per product | 10 |
| URL validation | Non-empty, absolute `http`/`https`, ≤500 chars — **no file-extension check**, so extensionless CDN links are accepted |
| Duplicate URLs | Rejected per product, compared trimmed and case-insensitively |
| Main image | At most one per product; the first image added becomes main; removing the main image promotes the next in gallery order; a product with no images has no main |
| Gallery order | `DisplayOrder`, then `CreatedAt` |
| Max attributes per product | 50 |
| Duplicate attribute names | Rejected per product, compared trimmed and case-insensitively |
| Attributes | `Name`/`Value` pairs; added, updated, removed or replaced as a set (admin panel S3); a name already in use on the product is rejected rather than overwritten |
| Description | Optional; trimmed, blank stored as `null`; editable through `PUT /api/v1/products/{id}` since admin panel S2 (omitted leaves it, blank clears it) |

Removing the extension allowlist was a deliberate trade for CDN support: nothing in the
domain asserts a URL points at an actual image, so a mistyped link fails visually at render
time rather than at the API boundary. Verifying content type would require a network call
from the domain, which is out of scope. Validation responsibility sits with the admin client.

Images and attributes can be supplied inline on `POST /api/v1/products` (one transaction —
a bad image rolls the whole product back), and images are separately editable through
sub-resource endpoints (`POST`/`DELETE .../images`, `PUT .../images/{imageId}/main`) with
`POST .../attributes` for attributes. All are `Admin`-only and covered by the gateway's
existing products write route. See
[Data Contracts](../01-overview/Data%20Contracts.md#catalog-service) for exact shapes.

Two enforcement details worth knowing:

- **"Exactly one main image" is guarded twice** — in the domain (`ProductImage`'s
  constructor and its `IsMain` setters are `internal`, so only `Product` can reach them)
  and in the database (a filtered unique index on `ProductImages (ProductId) WHERE IsMain`,
  which permits zero mains but makes two impossible). The integration suite has run on real
  PostgreSQL since Catalog audit Stage 0, so the index is exercised there.
- **`MainImageUrl` in list responses is a correlated subquery**, not a join or a
  denormalized column, ordered `IsMain` → `DisplayOrder` → `CreatedAt`. It relies on the
  composite `IX_ProductImages_ProductId (ProductId, IsMain, DisplayOrder) INCLUDE (Url)`
  index for an index-only scan, and is bounded by the page-size cap of 100.

Image and attribute mutations raise **no domain or integration events**, and image edits
stay stale in paged list results for up to the 5-minute cache TTL (the `products:list:*`
key family cannot be invalidated).

---

## Bulk Actions, Import and Export (admin panel S16)

All `Admin`-only, and all under a dedicated `bulk` rate-limit policy — 10 requests a minute per client by default
(`RateLimiting:Bulk:PermitLimit` / `WindowSeconds`), partitioned per client like `search`.

| Method | Path | Body / query | Answer |
|---|---|---|---|
| POST | `/api/v1/products/bulk/publish` | `{ productIds: [] }` | `BulkProductReport` |
| POST | `/api/v1/products/bulk/unpublish` | `{ productIds: [] }` | `BulkProductReport` |
| POST | `/api/v1/products/bulk/delete` | `{ productIds: [] }` (soft delete) | `BulkProductReport` |
| POST | `/api/v1/products/bulk/category` | `{ productIds: [], categoryId }` | `BulkProductReport` |
| POST | `/api/v1/products/bulk/price` | `{ items: [{ productId, price }] }` | `BulkProductReport` |
| POST | `/api/v1/products/import` | `{ products: [{ name, description, sku, price, stockQuantity, categoryId }] }` | `ProductImportReport` |
| GET | `/api/v1/products/export` | the admin list's filters and sort (`categoryId`, `searchTerm`, `minPrice`, `maxPrice`, `status`, `hasDiscount`, `stockBelow`, `createdFrom`, `createdTo`, `sortBy`, `isDescending`) | `text/csv` file |

How they behave:

- **Synchronous, hard-capped, one report entry per row** (decision Q5a). At most **1 000** ids, prices or import rows per
  request, and at most **10 000** exported rows. Over a cap the request is refused whole with 400 — never truncated.
- **A 200 always carries the per-row report**, even when every row was refused. Each entry names its product (or import
  row index), whether it succeeded, and if not an error code — `Product.NotFound`, `DomainError` (the product's own rule
  refused it, e.g. a price at or below an active discount), and for import `Validation.Failed`, `Product.SkuConflict` or
  `Category.NotFound`. A non-2xx means nothing was changed.
- **One transaction per request.** Every refusal is decided before the single save, so every row reported as succeeded is
  committed and no refused row changed anything. A database failure — a SKU taken by a concurrent create between the import's
  check and its save — fails the whole request (409 `Product.SkuConflict`) rather than a row.
- **Idempotent where the single endpoint is:** publishing a published product or unpublishing a draft is a success. A
  soft-deleted product is `Product.NotFound`, as it is to the single endpoints.
- **Import is create-only.** A row whose SKU a live product holds is refused, never merged; a SKU on two rows refuses both.
  Each row is checked by the same validator `POST /api/v1/products` uses, and new products are **drafts**. Rows are JSON:
  a client that edits the CSV export turns it back into rows. The export's columns carry every import field under the
  same name.
- **Bulk price takes absolute prices**, one per product, through `Product.UpdatePrice` — so every single-edit rule applies and
  a moved customer-facing price still raises `ProductPriceChangedEvent` for Basket.
- **The export** includes drafts and excludes soft-deleted products, follows the list's sort, and uses the shared CSV rules
  (RFC 4180 quoting, a leading apostrophe on any field a spreadsheet would run as a formula, UTF-8 with a BOM).
- **Cache:** each request bumps the `products:list` family **once** and evicts both detail variants of each named product.
- **Audit:** one `audit_log` row **per product** (or import row), each with its own outcome — see
  [Admin Audit Trail](../03-architecture/audit-log.md).

At the gateway the POSTs are covered by `catalog-products-write-route`; the export has its own
`catalog-products-export-route` (`Admin`, `Order: 19`), because `GET /api/v1/products/**` is otherwise anonymous there. The
import alone gets a larger request-body cap (`CatalogProxy:ImportMaxRequestBodySizeBytes`, 8 MiB).

A zone-less date in `createdFrom`/`createdTo` (`?createdFrom=2026-09-01`) is read as UTC by both the list and the export
since S16; before, the list answered 500 for it.

---

## Security and Access

Admin-reachable commands are recorded in this service's `audit_log` and served on `GET /api/v1/admin/audit`
(`audit.read`); see [Admin Audit Trail](../03-architecture/audit-log.md).

- JWT authentication support
- Role-based authorization for administrative writes
- CORS and rate-limiting alignment through service/gateway policies

---

## Health and Telemetry

Catalog service exposes health endpoints and emits:
- Structured logs
- OpenTelemetry traces/metrics
- Prometheus metrics endpoint support

---

## Operational Notes

- Keep product/category contract changes synchronized with gateway routing and client expectations.
- Keep cache TTL/invalidation strategy aligned with data freshness requirements.
- Validate performance-sensitive endpoints with telemetry after changes.

---

## Related Documents

- [API Gateway](api-gateway.md)
- [Basket Service](basket-service.md)
- [Infrastructure - Databases](../06-infrastructure/databases.md)
- [Infrastructure - Caching](../06-infrastructure/caching.md)

---

**Version**: 2.2  
**Last Updated**: 2026-09-16
