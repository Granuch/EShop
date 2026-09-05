# Frontend Data Contracts

Field-level reference for Entities/DTOs across all EShop services, for teams building a
client application (web/mobile) on top of the API Gateway. Compiled from the actual
repository code (`Domain/Entities`, `Application/.../Dto|Command|Query`), not from an
abstract schema.

---

## Store Vertical

By code, the platform is a **generic e-commerce framework** (similar to
eShopOnContainers): a product is modeled abstractly (`Name`, `SKU`, `Price`,
`CategoryId`, a set of `key/value` attributes), with no binding to a specific domain
(apparel/electronics/books/etc.) in the code itself.

However, the attribute model itself strongly hints at one — the `ProductAttribute`
comment explicitly gives the example `Size: Large`, `Color: Blue`. For documentation and
frontend purposes, the vertical is therefore fixed as **a fashion & footwear store**:

- **Categories** — hierarchical (`ParentCategoryId`), mapped to the structure
  `Men / Women / Kids -> Clothing / Shoes / Accessories`.
- **Product** — modeled as an apparel/footwear item: name, SKU, price, discount price,
  stock quantity, image gallery, status (`Draft/Active/Discontinued`).
- **ProductAttribute** — variant characteristics: `Size` (S/M/L/XL or a numeric shoe
  size), `Color`, `Material`, etc. Stored as `Name/Value` pairs, so new ones can be added
  without a schema change.
- **ProductImage** — the product card gallery; one image is flagged as main (`IsMain`)
  for catalog/list views.

This is a documentation convention only — it doesn't change backend code, only what
values are expected in `Name`/`CategoryId`/`ProductAttribute.Name` and how to render them
on the frontend (size/color selectors on the product card, category filter in the
catalog, etc.).

---

## How to Read This Document

- **Entity** — the domain model (what's persisted); not returned directly to the
  frontend, but defines which fields exist at all.
- **DTO** — what's actually sent/received over the API (use this for frontend types,
  e.g. TypeScript interfaces).
- **Command**/**Request** tables — the request body (`POST`/`PUT`) the frontend sends.
- All Ids are `Guid` (UUID string), except `Basket.UserId`/`Order.UserId`, which are
  `string` (Identity `UserId`), and refresh tokens, which are also `string`.
- Money fields (`Price`, `Amount`, `TotalPrice`, etc.) are `decimal`.

---

## Catalog Service

Base path: `/api/v1/products`, `/api/v1/categories`

### Product (entity)

| Field | Type | Required | Description |
|---|---|---|---|
| Id | Guid | — | Product identifier |
| Name | string | yes | Product name |
| Description | string? | no | Description |
| Sku | string | yes | SKU |
| Price | decimal | yes | Price (> 0) |
| DiscountPrice | decimal? | no | Discounted price |
| StockQuantity | int | yes | Stock on hand (>= 0) |
| Status | enum `ProductStatus` | — | `Draft` \| `Active` \| `Discontinued` |
| CategoryId | Guid | yes | Product category |
| Images | ProductImage[] | — | Image gallery |
| Attributes | ProductAttribute[] | — | `Name`/`Value` pairs (size, color, etc.) |
| IsDeleted | bool | — | Soft delete flag |

### ProductDto (response of `GET /api/v1/products`, list view)

| Field | Type | Description |
|---|---|---|
| Id | Guid | |
| Name | string | |
| Description | string? | |
| Sku | string | |
| Price | decimal | |
| DiscountPrice | decimal? | |
| StockQuantity | int | |
| Status | enum `ProductStatus` | |
| CategoryId | Guid | |
| MainImageUrl | string? | Direct link to the main image (for list/catalog cards) |
| CreatedAt | DateTime | |

> Note: full product detail (all images, all attributes) is not included in the list
> `ProductDto` — only `MainImageUrl`. If a dedicated product detail screen needs the
> full gallery and attributes, check with backend — currently list and detail share the
> same `ProductDto`.

### ProductImage (entity)

| Field | Type | Description |
|---|---|---|
| Id | Guid | |
| ProductId | Guid | |
| Url | string | Absolute HTTP/HTTPS URL; allowed extensions: jpg/jpeg/png/webp/gif |
| AltText | string? | Up to 200 characters |
| DisplayOrder | int | Sort order within the gallery |
| IsMain | bool | Main image flag |

### ProductAttribute (entity) — for size/color selectors

| Field | Type | Description |
|---|---|---|
| Id | Guid | |
| ProductId | Guid | |
| Name | string | Up to 100 characters (e.g. `Size`, `Color`, `Material`) |
| Value | string | Up to 200 characters (e.g. `L`, `Red`) |

### CreateProductCommand (`POST /api/v1/products`, admin)

| Field | Type | Required |
|---|---|---|
| Name | string | yes |
| Description | string? | no |
| Sku | string | yes |
| Price | decimal | yes (> 0) |
| StockQuantity | int | yes (>= 0) |
| CategoryId | Guid | yes |

### UpdateProductCommand (`PUT /api/v1/products/{id}`, admin)

| Field | Type | Required |
|---|---|---|
| Price | decimal | yes |
| StockQuantity | int | yes |

### Category (entity) / CategoryDto

| Field | Type | Description |
|---|---|---|
| Id | Guid | |
| Name | string | |
| Description | string? | |
| Slug | string | URL slug (auto-generated if not provided) |
| ParentCategoryId | Guid? | Parent category (for tree structure) |
| ParentCategoryName | string? | DTO-only — parent's name |
| DisplayOrder | int | Order within the list |
| IsActive | bool | |
| ChildCategories | CategoryDto[] | Child categories (recursive) |

### CreateCategoryCommand (`POST /api/v1/categories`, admin)

| Field | Type | Required |
|---|---|---|
| Name | string | yes |
| Slug | string? | no (auto-generated from Name) |
| ParentCategoryId | Guid? | no |

### UpdateCategoryCommand (`PUT /api/v1/categories/{id}`, admin)

| Field | Type | Required |
|---|---|---|
| Name | string | yes |
| Description | string | yes |

### List query parameters (`GET /api/v1/products`)

| Parameter | Type | Description |
|---|---|---|
| PageNumber | int? | Defaults to 1 |
| PageSize | int? | Defaults to 10 |
| CategoryId | Guid? | Filter by category |
| SearchTerm | string? | Search by name |
| MinPrice / MaxPrice | decimal? | Price range filter |
| SortBy | enum `ProductSortBy` | `Name` \| `Price` \| `CreatedAt` |
| IsDescending | bool? | Sort direction |
| Cursor | DateTime? | Keyset pagination cursor (instead of offset for deep pages) |

Other endpoints: `GET /api/v1/products/{id}`, `DELETE /api/v1/products/{id}` (admin),
`GET /api/v1/categories`, `GET /api/v1/categories/{id}`,
`GET /api/v1/categories/{id}/products`, `DELETE /api/v1/categories/{id}` (admin).

---

## Basket Service

Base path: `/api/v1/basket` (all operations are scoped by `{userId}`)

### BasketDto (`GET /api/v1/basket/{userId}`)

| Field | Type | Description |
|---|---|---|
| UserId | string | |
| Items | BasketItemDto[] | |
| TotalPrice | decimal | Sum across all line items |
| TotalItems | int | Total unit count |
| CreatedAt | DateTime | |
| LastModifiedAt | DateTime | |

### BasketItemDto

| Field | Type | Description |
|---|---|---|
| ProductId | Guid | |
| ProductName | string | Snapshot of the name at add time |
| Price | decimal | Snapshot of the price, kept in sync with catalog price changes |
| Quantity | int | |
| SubTotal | decimal | `Price * Quantity` |

> Basket item price is automatically updated via the `ProductPriceChangedEvent` from
> Catalog — the frontend doesn't need to recompute `Price` itself, it's always current
> as of the response.

### AddItemToBasketRequest (`POST /api/v1/basket/{userId}/items`)

| Field | Type | Required |
|---|---|---|
| ProductId | Guid | yes |
| Quantity | int | yes |

### UpdateBasketItemQuantityRequest (`PUT /api/v1/basket/{userId}/items/{productId}`)

| Field | Type | Required |
|---|---|---|
| Quantity | int | yes (0 or less removes the line item) |

### CheckoutBasketRequest (`POST /api/v1/basket/{userId}/checkout`)

| Field | Type | Required |
|---|---|---|
| ShippingAddress | string | yes |
| PaymentMethod | string | yes |

Other endpoints: `DELETE /api/v1/basket/{userId}/items/{productId}` (remove line item),
`DELETE /api/v1/basket/{userId}` (clear basket).

---

## Ordering Service

Base path: `/api/v1/orders`

### OrderDto (`GET /api/v1/orders/{id}`, `GET /api/v1/orders`)

| Field | Type | Description |
|---|---|---|
| Id | Guid | |
| UserId | string | |
| TotalPrice | decimal | |
| Status | enum `OrderStatus` | `Pending` \| `Paid` \| `Shipped` \| `Delivered` \| `Cancelled` \| `Refunded` |
| PaymentIntentId | string? | |
| CreatedAt | DateTime | |
| PaidAt / ShippedAt / DeliveredAt / CancelledAt | DateTime? | Status transition timestamps |
| CancellationReason | string? | |
| ShippingAddress | AddressDto | |
| Items | OrderItemDto[] | |

### AddressDto

| Field | Type | Description |
|---|---|---|
| Street | string | 3–150 characters |
| City | string | 2–100 characters |
| State | string | 2–100 characters |
| ZipCode | string | 3–12 characters (for `Country = "US"` — format `12345` or `12345-6789`) |
| Country | string | 2-letter ISO code (e.g. `UA`, `US`) |

### OrderItemDto

| Field | Type | Description |
|---|---|---|
| Id | Guid | |
| ProductId | Guid | |
| ProductName | string | Snapshot of the name at order time |
| UnitPrice | decimal | Snapshot of the price at order time |
| Quantity | int | |
| SubTotal | decimal | `UnitPrice * Quantity` |

### CreateOrderCommand (`POST /api/v1/orders`)

| Field | Type | Required |
|---|---|---|
| UserId | string | yes |
| Items | CreateOrderItemDto[] | yes, at least 1 |
| Street / City / State / ZipCode / Country | string | yes (see rules above) |

`CreateOrderItemDto`: `ProductId` (Guid), `ProductName` (string), `Price` (decimal),
`Quantity` (int).

> In practice, orders are usually created automatically from the
> `BasketCheckedOutEvent` after basket checkout — calling this directly from the
> frontend is less common.

### AddOrderItemCommand (`POST /api/v1/orders/{id}/items`)

| Field | Type | Required |
|---|---|---|
| ProductId | Guid | yes |
| ProductName | string | yes |
| UnitPrice | decimal | yes |
| Quantity | int | yes |

### CancelOrderRequest (`POST /api/v1/orders/{id}/cancel`)

| Field | Type | Required |
|---|---|---|
| Reason | string | yes |

Other endpoints: `GET /api/v1/orders?...` (paginated list),
`GET /api/v1/users/{userId}/orders` (a user's orders),
`DELETE /api/v1/orders/{id}/items/{itemId}`, `POST /api/v1/orders/{id}/ship` (admin).

---

## Payment Service

Base path: `/api/v1/payments`

### PaymentDto

| Field | Type | Description |
|---|---|---|
| Id | Guid | |
| OrderId | Guid | |
| UserId | string | |
| Amount | decimal | |
| Currency | string | Defaults to `USD` |
| PaymentMethod | string | Defaults to `Mock` (dev environment), Stripe in production |
| Status | string | `Pending` \| `Processing` \| `Success` \| `Failed` \| `Refunded` |
| PaymentIntentId | string? | Stripe PaymentIntent id |
| ErrorMessage | string? | |
| CreatedAt | DateTime | |
| ProcessedAt / UpdatedAt | DateTime? | |

### CreatePaymentIntentCommand (`POST /api/v1/payments/create-intent`)

| Field | Type | Required |
|---|---|---|
| OrderId | Guid | yes |
| UserId | string | yes |
| Amount | decimal | yes |
| Currency | string? | no (default `USD`) |
| Email | string? | no |

Response — `CreatePaymentIntentDto`: `PaymentId` (Guid), `PaymentIntentId` (string),
`ClientSecret` (string, for Stripe.js on the frontend), `Status` (string).

### CreatePaymentCommand (`POST /api/v1/payments`)

| Field | Type | Required |
|---|---|---|
| OrderId | Guid | yes |
| UserId | string | yes |
| Amount | decimal | yes |
| Currency | string? | no |
| PaymentMethod | string? | no |

### RefundPaymentCommand (`POST /api/v1/payments/{id}/refund`, admin)

| Field | Type | Required |
|---|---|---|
| Amount | decimal? | no (full refund if omitted) |
| Reason | string? | no |

Other endpoints: `GET /api/v1/payments/{id}`, `GET /api/v1/users/{userId}/payments`,
`GET /api/v1/payments/simulation` (mock-mode settings, for dev environments),
`POST /webhooks/stripe` (server-side webhook, not called by the frontend).

---

## Identity Service

Base path: `/api/v1/auth` (registration/login), `/api/v1/account` (profile)

### RegisterCommand (`POST /api/v1/auth/register`)

| Field | Type | Required |
|---|---|---|
| Email | string | yes |
| Password | string | yes |
| FirstName | string | yes |
| LastName | string | yes |

Response `RegisterResponse`: `UserId`, `Email`, `Message`.

### LoginCommand (`POST /api/v1/auth/login`)

| Field | Type | Required |
|---|---|---|
| Email | string | yes |
| Password | string | yes |
| TwoFactorCode | string? | no (when 2FA is enabled) |

Response `LoginResponse`:

| Field | Type | Description |
|---|---|---|
| AccessToken | string | JWT |
| RefreshToken | string | |
| ExpiresIn | int | Seconds until the access token expires |
| TokenType | string | `Bearer` |
| Requires2FA | bool | If true, a separate 2FA confirmation call is required |
| User | UserDto? | |

`UserDto`: `Id`, `Email`, `FirstName`, `LastName`, `Roles` (string[]).

### RefreshTokenCommand (`POST /api/v1/auth/refresh-token`)

| Field | Type | Required |
|---|---|---|
| RefreshToken | string | yes |

Response `RefreshTokenResponse`: `AccessToken`, `RefreshToken`, `ExpiresIn`.

### UserProfileResponse (`GET /api/v1/account/profile`)

| Field | Type | Description |
|---|---|---|
| Id | string | |
| Email | string | |
| FirstName | string | |
| LastName | string | |
| ProfilePictureUrl | string? | |
| EmailConfirmed | bool | |
| TwoFactorEnabled | bool | |
| IsActive | bool | |
| CreatedAt | DateTime | |
| LastLoginAt | DateTime? | |
| Roles | string[] | |

### UpdateProfileCommand (`PUT /api/v1/account/profile`)

| Field | Type | Required |
|---|---|---|
| FirstName | string | yes |
| LastName | string | yes |
| ProfilePictureUrl | string? | no |

### ChangePasswordCommand (`POST /api/v1/account/change-password`)

| Field | Type | Required |
|---|---|---|
| CurrentPassword | string | yes |
| NewPassword | string | yes |

Other endpoints: `POST /api/v1/auth/revoke-token`, `POST /api/v1/auth/confirm-email`,
`POST /api/v1/auth/forgot-password`, `POST /api/v1/auth/reset-password`,
`POST /api/v1/account/enable-2fa`, `POST /api/v1/account/verify-2fa`,
`POST /api/v1/account/disable-2fa`.

---

## Notification Service

Internal service (no direct frontend calls — driven by queue events). Only relevant for
displaying email delivery status in an admin panel.

### NotificationLog (entity)

| Field | Type | Description |
|---|---|---|
| Id | Guid | |
| EventType | string | The event type that triggered the notification |
| RecipientEmail | string | |
| TemplateName | string | |
| Subject | string | |
| Status | enum `NotificationStatus` | `Pending` \| `Sent` \| `Failed` |
| RetryCount | int | |
| SentAt | DateTime? | |

---

## Related Documents

- [Catalog Service](catalog-service.md)
- [Basket Service](basket-service.md)
- [Ordering Service](ordering-service.md)
- [Payment Service](payment-service.md)
- [Identity Service](identity-service.md)
- [Notification Service](notification-service.md)
- [Project Overview](../01-overview/project-overview.md)

---

**Version**: 1.0
**Last Updated**: 2026-09-05