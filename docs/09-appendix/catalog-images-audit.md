# Catalog — Product Images & Attributes Audit

Current-state audit of product image and attribute handling in the Catalog service,
as input to the "Variant A" (URL-based images and key/value attributes) effort.

Audit only — no source under `src/` was modified.

---

## 1. Scope & Method

| Item | Value |
|---|---|
| Service | `src/Services/Catalog` (Domain, Application, Infrastructure, API) |
| Cross-service | Basket, Ordering, Payment, Notification, Identity, API Gateway |
| Tests | `tests/Services/Catalog/**` |
| Branch | `feature/URL-based-image` |
| Date | 2026-09-05 |

Every claim below was read directly from the file cited. Items that could not be
verified are isolated in [§9](#9-unverifiable-items) rather than assumed.

**Headline finding.** The domain model for images and attributes is complete and
tested, but it is unreachable from outside the Domain layer. No command, handler,
endpoint, or seed row ever creates a `ProductImage` or `ProductAttribute`. Meanwhile
the public API advertises a `MainImageUrl` field that is hard-coded to `null` on every
list response.

---

## 2. Domain Layer

### 2.1 Entities

| Type | File | Base | Notes |
|---|---|---|---|
| `Product` | `EShop.Catalog.Domain/Entities/Product.cs` | `AggregateRoot<Guid>` | Private `_images`/`_attributes` lists exposed as `IReadOnlyCollection<>` (`:22-26`) |
| `ProductImage` | `EShop.Catalog.Domain/Entities/ProductImage.cs` | `Entity<Guid>` | Entity, not a value object — has its own `Id` |
| `ProductAttribute` | `EShop.Catalog.Domain/Entities/ProductAttribute.cs` | `Entity<Guid>` | Entity, not a value object |
| `ProductStatus` | `Product.cs:222-227` | enum | `Draft`, `Active`, `Discontinued` — declared inline, not a separate file |

`Category` (`Entities/Category.cs`) has no image concept; no `CategoryImage` type exists.

### 2.2 Validation rules

| Field | Rule | Location |
|---|---|---|
| `ProductImage.Url` | Non-empty; must parse as absolute URI with scheme `http`/`https` | `ProductImage.cs:61-69` |
| `ProductImage.Url` | Extension of `Uri.AbsolutePath` must be in `.jpg .jpeg .png .webp .gif` (case-insensitive) | `ProductImage.cs:11-19, 71-75` |
| `ProductImage.Url` | **No maximum length check** | `ProductImage.cs:59-78` |
| `ProductImage.AltText` | Optional; trimmed; max 200 chars | `ProductImage.cs:80-90` |
| `ProductImage.DisplayOrder` | `>= 0` (checked twice — also in `Product.AddImage`) | `ProductImage.cs:34-35`, `Product.cs:151-152` |
| `ProductAttribute.Name` | Non-empty; trimmed; max 100 chars | `ProductAttribute.cs:32-42` |
| `ProductAttribute.Value` | Non-empty; trimmed; max 200 chars | `ProductAttribute.cs:44-54` |
| `ProductAttribute` | **No uniqueness rule** on name or name/value pair | `ProductAttribute.cs`, `Product.cs:206-219` |

All failures throw `DomainException`
(`BuildingBlocks.Domain/Exceptions/DomainException.cs`) — the only exception type used.

Because the URL extension test runs against `Uri.AbsolutePath`, a query string is
tolerated (`…/a.jpg?w=800` passes) but an extensionless CDN URL (`…/img/abc123`) is
rejected. This constrains which image hosts Variant A can accept.

### 2.3 Mutator methods

| Method | Location | Behaviour |
|---|---|---|
| `AddImage(url, altText, displayOrder)` | `Product.cs:143-165` | Rejects if `IsDeleted`; silently returns on duplicate URL; auto-mains the first image |
| `SetMainImage(imageId)` | `Product.cs:167-182` | Rejects if `IsDeleted`; throws if id not found; unsets all, then sets target |
| `RemoveImage(imageId)` | `Product.cs:184-204` | Rejects if `IsDeleted`; throws if not found; promotes a successor if the main was removed |
| `AddAttribute(name, value)` | `Product.cs:206-219` | Rejects if `IsDeleted`; rejects empty name/value; no uniqueness check |

There is **no** `RemoveAttribute` or `UpdateAttribute` — attributes are append-only.
None of the four methods raises a domain event.

### 2.4 Behavioural reference — main-image lifecycle and aggregate boundary

Recorded in full so the Variant A plan can be designed without reopening these files.

**Every site that writes `IsMain`** — there are exactly four:

| Site | Location | Effect |
|---|---|---|
| `ProductImage` constructor | `ProductImage.cs:45` | `IsMain = false` unconditionally on every new image |
| `Product.AddImage` | `Product.cs:159-162` | Calls `SetAsMain()` **only** when `_images.Count == 0` |
| `Product.SetMainImage` | `Product.cs:176-181` | Loops all images calling `UnsetAsMain()`, then `SetAsMain()` on the target |
| `Product.RemoveImage` | `Product.cs:195-203` | If the removed image was main and ≥1 remains, promotes `OrderBy(DisplayOrder).ThenBy(CreatedAt).First()` |

Verbatim, `Product.cs:154-164`:

```csharp
if (_images.Any(x => x.Url == url))
    return;

var newImage = new ProductImage(Id ,url, altText, displayOrder);

if (_images.Count == 0)
{
    newImage.SetAsMain();
}

_images.Add(newImage);
```

Verbatim, `ProductImage.cs:49-57`:

```csharp
public void SetAsMain()  { IsMain = true; }
public void UnsetAsMain(){ IsMain = false; }
```

Consequences the plan must design against:

- **The "exactly one main" property holds on the happy path but is not enforced.** It
  is an emergent result of three cooperating methods, not an invariant. Three ways to
  break it: `UnsetAsMain()` is public and unguarded, leaving **zero** mains;
  `SetAsMain()` is equally public, yielding **two** mains; and constructing a
  `ProductImage` directly bypasses `AddImage` entirely. There is no validation method,
  no save-time assertion, and no DB constraint — `CatalogDbContext.cs:139-153`
  configures `ProductImages` with only a PK, `Url`, `AltText`, and a **non-unique**
  index on `ProductId`.
- **Zero images must remain legal.** `RemoveImage` promotes a successor only when
  images remain (`Product.cs:195`), so removing the last image correctly leaves no
  main. Any new invariant has to permit "zero images ⇒ zero mains".
- **The duplicate check compares un-normalized input.** `Product.cs:154` tests
  `x.Url == url` — ordinal, case-sensitive, against the **raw** argument, not the
  trimmed form `NormalizeAndValidateUrl` (`ProductImage.cs:59-78`) will store. So
  `" https://x/a.jpg"` and `"https://x/a.jpg"` pass the check as distinct but collide
  after normalization, and `…/A.JPG` vs `…/a.jpg` are always distinct. The caller
  cannot tell "added" from "silently ignored" — the method returns `void`.
- **Ordering is ambiguous and inconsistent.** `DisplayOrder` is caller-supplied with
  no uniqueness or contiguity rule, so ties are possible. `RemoveImage` breaks ties
  with `.ThenBy(i => i.CreatedAt)` (`Product.cs:197-200`); the Mapster projection uses
  `.OrderBy(i => i.DisplayOrder)` with **no** tiebreaker (`MappingConfig.cs:18-21`).
- **Aggregate boundary is convention-only.** `ProductImage.cs:29` and
  `ProductAttribute.cs:17` both expose `public` constructors. No current code exploits
  this: the only `new ProductImage(…)` / `new ProductAttribute(…)` sites in the repo
  are `Product.cs:157` and `Product.cs:217`.

### 2.5 Domain events

`Events/`: `ProductCreatedEvent`, `ProductPriceChangedEvent`, `ProductOutOfStockEvent`,
`ProductBackInStockEvent`. **None** relates to images or attributes, and neither
`Publish()` nor `SoftDelete()` raises one.

---

## 3. Application Layer

### 3.1 Inventory

| Artefact | Path (under `EShop.Catalog.Application/`) | Touches images/attributes? |
|---|---|---|
| `CreateProductCommand` + handler + validator | `Products/Commands/CreateProduct/` | No |
| `UpdateProductCommand` + handler + validator | `Products/Commands/UpdateProduct/` | No |
| `DeleteProductCommand` + handler + validator | `Products/Commands/DeleteProduct/` | No |
| `GetProductsQuery` + handler + validator | `Products/Queries/GetProducts/` | Returns `MainImageUrl`, always `null` |
| `GetProductByIdQuery` + handler + validator | `Products/Queries/GetProductsById/` | Returns `MainImageUrl` via Mapster |
| `GetProductByCategoryQuery` + handler + validator | `Products/Queries/GetProductByCategory/` | Returns `MainImageUrl`, always `null` |
| `ProductCreatedEventHandler` | `Products/EventHandlers/` | No |
| `ProductPriceChangedEventHandler` | `Products/EventHandlers/` | No |
| `MappingConfig` | `Mapping/MappingConfig.cs` | Computes `MainImageUrl` |

There is no command, query, DTO, or validator anywhere for images or attributes.

### 3.2 Command contracts

`CreateProductCommand` (`…/CreateProduct/CreateProductCommand.cs:13-19`) —
`Name`, `Description`, `Sku`, `Price`, `StockQuantity`, `CategoryId`.

`UpdateProductCommand` (`…/UpdateProduct/UpdateProductCommand.cs:9-12`) —
`ProductId`, `Price`, `StockQuantity`.

**Neither accepts images or attributes.** Both implement `IRequest<>`,
`ICacheInvalidatingCommand`, and `ITransactionalCommand`. Commands are bound directly
from the request body; there is no separate request-DTO layer.

### 3.3 Dead domain methods

A repo-wide search for `AddImage`, `RemoveImage`, `SetMainImage`, `AddAttribute`
returns matches **only** inside `Product.cs` — the four definitions plus one internal
call (`SetMainImage` from `RemoveImage` at `:202`). There are no call sites in
Application, Infrastructure, or API. These methods are reachable only from unit tests.

### 3.4 `ProductDto`

`Products/Queries/GetProducts/ProductDto.cs:8-21` — 11 fields:

```
Id, Name, Description, Sku, Price, DiscountPrice, StockQuantity,
Status, CategoryId, MainImageUrl, CreatedAt
```

**One DTO serves all three read paths** — `GetProductsQuery` returns
`PagedResult<ProductDto>`, `GetProductByIdQuery` returns `ProductDto`, and
`GetProductByCategoryQuery` returns `List<ProductDto>`. There is no
`ProductListItemDto`/`ProductDetailsDto` split, no `Images[]`, and no `Attributes[]`.
The XML comment calls it "DTO for product in list view" despite detail reusing it.

### 3.5 Validator conventions

Rules Variant A validators should match:

| Validator | Rules |
|---|---|
| `CreateProductCommandValidator` | `Name` NotEmpty + MaxLength 200; `Sku` NotEmpty + MaxLength 50 + regex `^[A-Za-z0-9\-_]+$`; `Price` > 0; `StockQuantity` >= 0; `CategoryId` NotEmpty |
| `UpdateProductCommandValidator` | `ProductId` NotEmpty; `Price` > 0; `StockQuantity` >= 0 |
| `GetProductsQueryValidator` | Page >= 1; page size 1–100; `MinPrice` >= 0; `MaxPrice` > `MinPrice`; `SearchTerm` 2–200 |

Convention: one `AbstractValidator<T>` per command/query, in the same folder, with
`.WithMessage(...)` on every rule. Note that validator limits are not always mirrored
in the domain — `Product.Create` checks only non-emptiness for `Name`/`Sku`.

### 3.6 Caching

| Key | Declared in |
|---|---|
| `product:{ProductId}` | `GetProductByIdQuery.cs:15` |
| `products:category:{CategoryId}` | `GetProductByCategoryQuery.cs:12` |
| `products:list:cat=…:s=…:min=…:max=…:sort=…:desc=…:p=…:ps=…:cur=…` | `GetProductsQuery.cs:44-45` |
| `categories:all` | `GetCategoriesQuery.cs:13` |
| `category:{Id}` | `GetCategoryByIdQuery.cs:11` |

Invalidation: `CreateProductCommand` declares `products:category:{CategoryId}`;
`UpdateProductCommand` and `DeleteProductCommand` declare `product:{ProductId}` and
additionally invalidate `products:category:{…}` at runtime once the aggregate is
loaded (`UpdateProductCommandHandler.cs:44`, `DeleteProductCommandHandler.cs:43`).

`ICacheInvalidatingCommand` supports **exact keys only — no wildcards**
(`BuildingBlocks.Application/Caching/ICacheableQuery.cs:57-61`). Because the list key
embeds every filter, sort, and paging parameter, the `products:list:*` family can
never be targeted and stays stale for its full TTL after any write.

---

## 4. Infrastructure Layer

### 4.1 EF Core configuration

There are **no** `IEntityTypeConfiguration<T>` classes in this service. All mapping is
inline in `CatalogDbContext.OnModelCreating`
(`EShop.Catalog.Infrastructure/Data/CatalogDbContext.cs`).

| Entity | Config block | Table | Notes |
|---|---|---|---|
| `Product` | `:41-99` | `Products` | `Version` is a concurrency token (`:62-63`); unique `Sku`; GIN trigram indexes on `Name`/`Sku` |
| `Category` | `:101-137` | `Categories` | `Version` concurrency token (`:116-117`) |
| `ProductImage` | `:139-153` | `ProductImages` | `Url` required `varchar(500)`; `AltText` `varchar(200)`; **non-unique** index on `ProductId` |
| `ProductAttribute` | **absent** | `ProductAttribute` | No config block exists — verified zero `Entity<ProductAttribute>` occurrences |

Relationships (`:88-96`): `Product.Images` and `Product.Attributes` are both
`HasMany().WithOne().OnDelete(DeleteBehavior.Cascade)` with no inverse navigation —
separate tables, not owned entities.

Global query filters: `Product` → `!p.IsDeleted` (`:98`), `Category` → `c.IsActive`
(`:136`).

Because `ProductAttribute` has no config block, EF falls back to convention. The model
snapshot confirms the result: `b.ToTable("ProductAttribute")`
(`Migrations/CatalogDbContextModelSnapshot.cs:284`) — singular, inconsistent with
`ProductImages` — and `Name` mapped as bare `text` with no length
(`:263-265`), so the domain's 100/200-character limits have no DB-level backing.

`DbSet`s: `Products`, `Categories`, `ProductImages`. `ProductAttribute` is **not**
exposed as a `DbSet` and is reachable only through the navigation property.

### 4.2 Read paths

`Repositories/ProductRepository.cs`:

| Method | Includes | Tracking |
|---|---|---|
| `GetByIdAsync` (`:17`) | `Category`, `Images`, `Attributes`, `AsSplitQuery` (`:20-23`) | Tracked |
| `GetByIdReadOnlyAsync` (`:27`) | `Category`, `Images`, `Attributes`, `AsNoTracking`, `AsSplitQuery` (`:30-34`) | No-tracking |
| `GetBySkuAsync` (`:38`) | None | Tracked |
| `GetByCategoryAsync` (`:44`) | None, `AsNoTracking` | No-tracking |
| `Query()` (`:73`) | None, `AsNoTracking` | No-tracking |

`QueryServices/ProductQueryService.cs` — pure EF LINQ projections, no raw SQL or
Dapper. `GetFilteredProductsAsync` supports `ILike` search, both cursor (keyset on
`CreatedAt`) and offset pagination, and a separate `CountAsync`.

Both projections hard-code the image field. `ProductQueryService.cs:79-81`:

```
// Select projection — only fetch columns needed for DTO (avoids over-fetching).
// Images are NOT included in the listing to eliminate the LEFT JOIN overhead
// on the ProductImages table (3.2M unnecessary index scans under load).
```

followed by `MainImageUrl = null` at `:94` (`GetFilteredProductsAsync`) and again at
`:138` (`GetProductsByCategoryAsync`).

### 4.3 Mapster

`EShop.Catalog.Application/Mapping/MappingConfig.cs:16-21`:

```csharp
config.NewConfig<Product, ProductDto>()
    .Map(dest => dest.MainImageUrl,
        src => src.Images
            .OrderBy(i => i.DisplayOrder)
            .Select(i => i.Url)
            .FirstOrDefault());
```

`IsMain` is never referenced. `MainImageUrl` is derived purely from the lowest
`DisplayOrder`, with no tiebreaker, so `SetMainImage` has **no observable effect on any
API response**. Neither `Images` nor `Attributes` is mapped to any DTO collection.

This mapping is exercised only by `GetProductByIdQueryHandler`; the list paths bypass
Mapster entirely and use the `ProductQueryService` projections above.

### 4.4 Seed data

No seed data exists. No `HasData`, seeder, initializer, or SQL script creates a
`ProductImage` or `ProductAttribute` row.

---

## 5. API Layer

### 5.1 Product endpoints — `EShop.Catalog.API/Endpoints/ProductEndpoints.cs`

Group: `/api/v1/products` (`:20`).

| Verb / Route | Line | Request | Response | Auth | Rate limit |
|---|---|---|---|---|---|
| `GET /` | `:24` | `[AsParameters] GetProductsQuery` | `PagedResult<ProductDto>` / 400 | Anonymous | `search` (`:36`) |
| `GET /{id:guid}` | `:41` | route `id` | `ProductDto` / 404 | Anonymous | None |
| `POST /` | `:57` | `CreateProductCommand` | 201 / 400 | `Admin` (`:69`) | None |
| `PUT /{id:guid}` | `:74` | `UpdateProductCommand` | 204 / 400 | `Admin` (`:92`) | None |
| `DELETE /{id:guid}` | `:97` | route `id` | 204 / 404 | `Admin` (`:109`) | None |

Category endpoints (`Endpoints/CategoryEndpoints.cs`) follow the same shape: three
anonymous `GET`s (including `/{id}/products`) and `Admin`-gated `POST`/`PUT`/`DELETE`.

**No endpoint exists** for adding, removing, or reordering images, setting the main
image, or adding/removing attributes.

### 5.2 Conventions to match

Extension method per feature, `app.MapGroup("/api/v1/…").WithTags(…)`, handlers
returning `Result<T>` and unwrapped via `result.Match(onSuccess, onError)` into
`Results.Ok`/`Created`/`Problem`. Validation is handled upstream by
`ValidationBehavior<,>` in the MediatR pipeline, not in the endpoint. Caching is
pipeline-based — there are no output-cache policies anywhere in the service.

### 5.3 Upload and storage capability

A repo-wide search for `IFormFile`, `multipart/form-data`, `UseStaticFiles`,
`wwwroot`, `AmazonS3`, `MinIO`, `Azure.Storage.Blobs`, and `Cloudinary` — across both
`.cs` sources and `.csproj` package references — returns **zero hits**. There is no
file-upload, static-file-serving, or object-storage capability anywhere in the
repository. This confirms Variant A's premise: URLs are the only viable image input
today.

---

## 6. Cross-Service Impact

### 6.1 Integration events

| Event | Fields | Publisher | Consumers |
|---|---|---|---|
| `ProductCreatedIntegrationEvent` | `ProductId`, `ProductName`, `Price`, `CategoryId` | `ProductCreatedEventHandler` | `Identity/…/Consumers/ProductCreatedConsumer.cs` — logs only (`:31-37`) |
| `ProductPriceChangedIntegrationEvent` | `ProductId`, `OldPrice`, `NewPrice` | `ProductPriceChangedEventHandler` | `Basket/…/Consumers/ProductPriceChangedConsumer.cs` — re-prices live baskets |
| `ProductDeletedIntegrationEvent` | `ProductId` | **None** | **None** |

All three live in `BuildingBlocks.Messaging/Events/`. **No event carries image,
thumbnail, or picture data**, so adding images to Catalog requires no contract change
and breaks no consumer.

`ProductDeletedIntegrationEvent` is a dead contract — defined but never published or
consumed. `Product.SoftDelete()` raises no domain event to trigger it.

### 6.2 Other services

Searches across Basket, Ordering, Payment, and Notification for `ImageUrl`,
`PictureUrl`, `Thumbnail`, and `Image` return no product-image references. Basket
items carry `ProductId`/`ProductName`/`Price`/`Quantity`; order items carry
`ProductId`/`ProductName`/`UnitPrice`/`Quantity`. Notification consumes no product
event at all.

**Variant A is contained within Catalog.** No downstream service needs to change.

### 6.3 API Gateway

`src/ApiGateways/EShop.ApiGateway/appsettings.json` (`ReverseProxy` at `:131`):
catalog write routes (`POST`/`PUT`/`PATCH`/`DELETE`) carry
`AuthorizationPolicy: "Admin"`; read routes (`GET`/`HEAD`/`OPTIONS`) carry no policy
and are public. New admin image/attribute endpoints under `/api/v1/products/**` are
therefore **already covered** by the existing catch-all write route — no gateway
change is required, though the service must still enforce `Admin` itself.

Rate limiting at the gateway is global only (a single IP-partitioned limiter); no
per-route policy exists there.

### 6.4 Documentation

`docs/01-overview/Data Contracts.md` documents `Product.Images[]` and
`Product.Attributes[]` on the entity and `MainImageUrl` on `ProductDto`, and notes
that the full gallery is absent from the list DTO. It does **not** state that
`MainImageUrl` is always `null` in practice, so a frontend built against it will
target a field that never has a value.

`docs/05-services/catalog-service.md` does not mention images or attributes at all.

---

## 7. Tests

All image and attribute coverage lives in a single file:
`tests/Services/Catalog/EShop.Catalog.UnitTests/Domain/ProductTests.cs` — the
`#region Images` block (`:340-488`, 11 tests) and `#region Attributes` (`:490-563`,
6 tests), 41 references to the four mutators in total. Every one is a **domain-level**
unit test against the aggregate.

There is **no** handler-level, mapping-level, or HTTP-level test touching images or
attributes, and no test anywhere asserts a `MainImageUrl` value — so neither the
hard-coded `null` nor the `IsMain`-ignoring mapping would be caught by the suite.
`CatalogDtos.cs` declares a `MainImageUrl` field on the test response model but never
asserts against it.

Conventions for new tests: NUnit `[TestFixture]`/`[Test]`,
`MethodUnderTest_Scenario_ExpectedOutcome` naming, `Assert.That(...)` constraints and
Moq in unit tests, FluentAssertions plus `WebApplicationFactory<Program>` in
integration tests. Catalog integration tests run on **EF InMemory**
(`Fixtures/CatalogApiFactory.cs:51`), not Postgres — so column length limits, unique
indexes, and cascade deletes cannot be verified there.

---

## 8. Known Gaps

| # | Gap | Location | Impact | Severity |
|---|---|---|---|---|
| 1 | `MainImageUrl` hard-coded `null` in both list projections | `ProductQueryService.cs:94,138` | Every list and by-category response returns `null`; no image can render in any listing | High |
| 2 | No application or API surface for images/attributes | Catalog Application; `ProductEndpoints.cs` | Admins cannot create image or attribute data at all; four domain methods are dead code | High |
| 3 | `ProductDto` exposes no `Images[]` or `Attributes[]` | `ProductDto.cs:8-21` | Detail view cannot render a gallery or spec table even though both are eagerly loaded | High |
| 4 | Mapster orders by `DisplayOrder` and ignores `IsMain` | `MappingConfig.cs:16-21` | `IsMain` has no observable effect on any response; `SetMainImage` is invisible to clients | High |
| 5 | `ProductAttribute` has no EF configuration | `CatalogDbContext.cs` (absent); snapshot `:263-265, 284` | Singular table name inconsistent with `ProductImages`; `Name`/`Value` unbounded `text`, so domain limits have no DB backing | Medium |
| 6 | `ProductImage.Url` has no length validation but the column is `varchar(500)` | `ProductImage.cs:59-78` vs `CatalogDbContext.cs:145-147` | A >500-char URL surfaces as `DbUpdateException` (500) instead of `DomainException` (400) | Medium |
| 7 | `products:list:*` cache family cannot be invalidated | `GetProductsQuery.cs:44-45`; `ICacheableQuery.cs:57-61` | Writes leave list results stale for the full TTL; will apply to image changes too | Medium |
| 8 | `GetByIdReadOnlyAsync` loads Images + Attributes that are then discarded | `ProductRepository.cs:27-34` + `MappingConfig.cs` | Two extra split queries per detail request for data that never reaches the response | Medium |
| 9 | No enforced "exactly one main image" invariant | `Product.cs:143-165`; `ProductImage.cs:49-57` | Property holds on the happy path only; public `SetAsMain`/`UnsetAsMain` and the public constructor can produce zero or multiple mains | Medium |
| 10 | `AddImage` silently no-ops on duplicate URL, comparing un-normalized input | `Product.cs:154-155` | Caller gets success with nothing added; whitespace/case variants evade the check then collide after normalization | Medium |
| 11 | No `RemoveAttribute` or `UpdateAttribute` | `Product.cs` | Attributes are append-only; a typo is permanent | Medium |
| 12 | No test asserts `MainImageUrl` or covers the Mapster config | `tests/Services/Catalog/**` | Gaps 1 and 4 are invisible to the suite | Medium |
| 13 | `Data Contracts.md` documents `MainImageUrl` without noting it is always null | `docs/01-overview/Data Contracts.md` | Frontend builds against a field that never has a value | Medium |
| 14 | `ProductImage`/`ProductAttribute` constructors are `public` | `ProductImage.cs:29`; `ProductAttribute.cs:17` | Aggregate boundary is convention-only; `AddImage` dedupe and auto-main logic are bypassable | Low |
| 15 | `DisplayOrder` has no uniqueness rule and tiebreaking is inconsistent | `Product.cs:197-200` vs `MappingConfig.cs:18-21` | Ties order non-deterministically in the mapping path | Low |
| 16 | No maximum image count per product | `Product.cs:143-165` | Unbounded growth and unbounded detail payload | Low |
| 17 | Image/attribute mutations raise no domain events | `Product.cs:143-219` | No hook for cache invalidation or future integration events | Low |
| 18 | `ProductDeletedIntegrationEvent` is never published or consumed | `BuildingBlocks.Messaging/Events/` | Dead contract; downstream services never learn of deletions | Low |
| 19 | Catalog integration tests run on EF InMemory | `CatalogApiFactory.cs:51` | Column limits, unique indexes, and cascade deletes are untestable in integration tests | Low |

---

## 9. Unverifiable Items

- **Frontend expectations.** `src/ClientApp/eshop-web` contains only `node_modules/`
  and `.next/` and has no git-tracked files (`git ls-files -- src/ClientApp` returns
  nothing). The actual client's expected product shape could not be verified from the
  repository; `docs/01-overview/Data Contracts.md` is the only available statement of
  intent.
- **`Product.DiscountPrice`.** The property exists and is mapped, but no domain method
  sets it. Whether it is ever populated could not be established from code alone.
- **Original intent of `MainImageUrl = null`.** The comment at
  `ProductQueryService.cs:79-81` cites a concrete figure ("3.2M unnecessary index
  scans under load"), but no benchmark, issue, or ADR supporting it was found in the
  repository. Treat the figure as unverified when weighing the join-versus-denormalize
  decision in Variant A.

---

## 10. Next Step

Part 2 — the Variant A implementation plan — is written separately to
`docs/09-appendix/catalog-images-variant-a-plan.md`, using this document as its source
of truth.
