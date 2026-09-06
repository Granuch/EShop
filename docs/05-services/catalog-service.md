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
| Attributes | `Name`/`Value` pairs, add-only — no update or remove operation exists, so a name already in use is rejected rather than overwritten |
| Description | Optional; set only at creation, trimmed, blank stored as `null` — no endpoint changes it afterwards |

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
  which permits zero mains but makes two impossible). The index is not exercised by the
  test suite, which runs on EF InMemory.
- **`MainImageUrl` in list responses is a correlated subquery**, not a join or a
  denormalized column, ordered `IsMain` → `DisplayOrder` → `CreatedAt`. It relies on the
  composite `IX_ProductImages_ProductId (ProductId, IsMain, DisplayOrder) INCLUDE (Url)`
  index for an index-only scan, and is bounded by the page-size cap of 100.

Image and attribute mutations raise **no domain or integration events**, and image edits
stay stale in paged list results for up to the 5-minute cache TTL (the `products:list:*`
key family cannot be invalidated).

---

## Security and Access

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

**Version**: 2.1  
**Last Updated**: 2026-09-06
