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

**Version**: 2.0  
**Last Updated**: 2026-04-14
