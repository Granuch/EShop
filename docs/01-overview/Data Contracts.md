# Frontend Data Contracts (moved)

This document has been replaced by the **[frontend API contracts](frontend/README.md)** in `docs/01-overview/frontend/`.
The new set is verified against the code and the running API, covers every endpoint for the storefront and the admin
panel, and gives each DTO a TypeScript type.

The old text is no longer maintained. It had drifted from the code in several ways:
- PascalCase field names, where the API sends camelCase;
- enum names where Catalog and Ordering send integers;
- an outdated payment status list and two-factor flow;
- no error, paging or permission model.

Use git history if you need the old wording. The sections that used to be here now live at the links below. The
"Store Vertical" section was dropped: the documentation is generic, and the seed data is placeholder content.

---

## How to Read This Document

See [README — How to read these files](frontend/README.md#how-to-read-these-files) and
[conventions.md](frontend/conventions.md) (JSON, ids, dates, money, enums, errors, paging).

## Catalog Service

| Old section | New location |
|---|---|
| Product, ProductDto, ProductDetailsDto | [catalog.md](frontend/catalog.md) |
| ProductImage, ProductAttribute and their admin sub-resources | [catalog.md](frontend/catalog.md) |
| CreateProductCommand, UpdateProductCommand | [catalog.md](frontend/catalog.md) |
| Category, CategoryDto, category commands | [catalog.md](frontend/catalog.md) |
| List query parameters | [catalog.md](frontend/catalog.md), and [conventions.md §6](frontend/conventions.md#6-paging) for paging |

## Basket Service

| Old section | New location |
|---|---|
| BasketDto, BasketItemDto | [basket.md](frontend/basket.md) |
| Add item, update quantity, checkout requests | [basket.md](frontend/basket.md) |

## Ordering Service

| Old section | New location |
|---|---|
| OrderDto, AddressDto, OrderItemDto | [ordering.md](frontend/ordering.md) |
| CreateOrderCommand, AddOrderItemCommand, CancelOrderRequest | [ordering.md](frontend/ordering.md) |

## Payment Service

| Old section | New location |
|---|---|
| PaymentDto | [payment.md](frontend/payment.md) |
| CreatePaymentIntentCommand, CreatePaymentCommand, RefundPaymentCommand | [payment.md](frontend/payment.md) |

## Identity Service

| Old section | New location |
|---|---|
| Register, login (including two-factor), refresh token | [identity.md](frontend/identity.md), overview in [conventions.md §4](frontend/conventions.md#4-authentication) |
| UserProfileResponse, UpdateProfileCommand, ChangePasswordCommand | [identity.md](frontend/identity.md) |

## Notification Service

| Old section | New location |
|---|---|
| NotificationLog | [notification.md](frontend/notification.md). Notification now has an admin HTTP API |

## Related Documents

- [Frontend API contracts](frontend/README.md)
- [Service documentation](../05-services/)
- [Project Overview](project-overview.md)

---

**Version**: 2.0 (redirect)  
**Last Updated**: 2026-09-23
