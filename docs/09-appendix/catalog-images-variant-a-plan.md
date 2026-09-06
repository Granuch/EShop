# Catalog — Variant A Implementation Plan

Implementation plan for URL-based product images and key/value attributes in the
Catalog service. Derived from [catalog-images-audit.md](catalog-images-audit.md);
gap numbers below refer to that document's §8 table.

**Variant A only.** No file upload, no object storage (S3/MinIO/Azure Blob), no virus
scanning, no image resizing or CDN integration — those belong to a separate Variant B
and are deliberately absent here.

---

## 1. Scope

| Decision | Answer |
|---|---|
| Contract shape | Inline `Images[]`/`Attributes[]` on create **and** sub-resource endpoints for later edits |
| Mutations in scope | Add image, remove image, set main image, add attribute |
| Mutations out of scope | Reorder images; remove/update attributes (gap #11 stays open by decision) |
| Max images per product | 10 |
| Image URL validation | Absolute `http`/`https`, ≤500 chars — **no file-extension requirement** |

Architectural patterns are preserved exactly: CQRS via MediatR, one
`AbstractValidator<T>` per command, Mapster for entity→DTO mapping, `IUnitOfWork` +
`ITransactionalCommand` for writes, `ICacheableQuery`/`ICacheInvalidatingCommand` for
Redis, and the minimal-API style of `ProductEndpoints.cs`.

---

## 2. Decision: list-view `MainImageUrl`

The list projections hard-code `MainImageUrl = null` (gap #1). Two ways out:

| Criterion | Subquery in list query | Denormalize onto `Products` |
|---|---|---|
| Read cost | One index-backed lookup per row, bounded by page size (≤100; category path ≤200) | Zero |
| Migration | None | New column + backfill |
| Write-path cost | None | Every image mutation must recompute and persist |
| Correctness | Correct by construction | Second source of truth; can drift |
| Reversibility | Denormalizing later is purely additive | Removing later means another migration |

**Recommendation: subquery.** Three reasons.

1. The performance justification for the current `null` is unverified. Audit §9 records
   that the "3.2M unnecessary index scans" comment has no benchmark, issue, or ADR
   behind it. An architectural workaround should not be preserved for an unmeasured
   problem.
2. Page size is capped at 100 by `GetProductsQueryValidator`, so the worst case is 100
   index lookups against `IX_ProductImages_ProductId` — not a scaling risk.
3. This plan builds the entire write path from scratch. Coupling every new mutator to a
   cached column on day one adds drift risk exactly where the code is least settled.

Mitigation instead of denormalization: widen the index to
`(ProductId, IsMain, DisplayOrder) INCLUDE (Url)` so the subquery becomes an index-only
scan — most of the benefit, none of the sync cost.

If a benchmark later shows the subquery is hot, denormalizing is a strictly additive
follow-up (add column, backfill, swap the projection) that invalidates none of this work.

---

## 3. Decision: `IsMain` enforcement

Audit §2.4 establishes that "exactly one main image" currently holds on the happy path
but is not enforced — it is an emergent property of three cooperating methods,
breakable via the public `SetAsMain()`/`UnsetAsMain()` or the public constructor
(gaps #9, #14).

**Both layers, not either.**

- **Domain (primary).** Make `ProductImage`'s constructor and both flag setters
  `internal`, so only `Product` — same assembly — can reach them. This converts the
  emergent property into a real invariant without rewriting the three methods, which
  are already correct.
- **Database (backstop).** Add a filtered unique index:

  ```sql
  CREATE UNIQUE INDEX "IX_ProductImages_ProductId_IsMain"
      ON "ProductImages" ("ProductId") WHERE "IsMain";
  ```

  This permits zero mains — required, since audit §2.4 notes that removing the last
  image legitimately leaves none — while making two mains impossible.

The domain guard produces a clean `DomainException`/400; the index catches drift and
concurrent writes the aggregate cannot see. Note the index will **not** be exercised by
the suite: Catalog integration tests run on EF InMemory (gap #19).

### Canonical ordering

One rule everywhere, replacing today's inconsistency (gap #15):

| Purpose | Ordering |
|---|---|
| Main image pick | `OrderByDescending(IsMain).ThenBy(DisplayOrder).ThenBy(CreatedAt)` → first |
| Gallery order | `OrderBy(DisplayOrder).ThenBy(CreatedAt)` |

Gallery order already matches `RemoveImage`'s successor promotion, so no domain change
is needed there. `IsMain` is exposed as a flag so the client decides how to surface it.

---

## 4. Tasks

### T1 — Close the aggregate boundary and fix `AddImage` semantics

**Layer:** Domain · **Effort:** M · **Depends on:** —

Files: `EShop.Catalog.Domain/Entities/ProductImage.cs`,
`EShop.Catalog.Domain/Entities/ProductAttribute.cs`,
`EShop.Catalog.Domain/Entities/Product.cs`,
`tests/Services/Catalog/EShop.Catalog.UnitTests/Domain/ProductTests.cs`

- `ProductImage`: constructor → `internal`; `SetAsMain()`/`UnsetAsMain()` → `internal`
  (gaps #9, #14).
- `ProductImage`: add a max-length 500 check in `NormalizeAndValidateUrl` to match the
  column, turning gap #6 from a 500 into a 400.
- `ProductImage`: **remove the extension allowlist** — delete the `AllowedExtensions`
  HashSet (`:11-19`) and the extension check (`:71-75`), leaving
  `NormalizeAndValidateUrl` enforcing only "non-empty, absolute `http`/`https`,
  ≤500 chars". This admits extensionless CDN links.

  Trade-off to record in a code comment: the domain no longer asserts the URL points at
  an image at all, so a mistyped link fails visually at render time rather than at the
  API boundary. Validation responsibility moves to the admin client. Verifying content
  type would require a network call from the domain, which is out of scope.
- `ProductAttribute`: constructor → `internal`.
- `Product.AddImage`: normalize the URL *before* the duplicate check and compare
  case-insensitively (gap #10); **throw** `DomainException` on duplicate instead of
  silently returning; enforce max 10 images (gap #16); **return the new image's `Guid`**
  so the API can answer 201 with an id.
- `Product.AddAttribute`: return the new attribute's `Guid`, same reason.

**Two existing tests break** (both in `ProductTests.cs`, Images region `:340-488`) and
must be rewritten, not deleted:

| Test | Current assertion | New assertion |
|---|---|---|
| `AddImage_DuplicateUrl_ShouldNotAddDuplicate` (`:393`) | Silent no-op leaves one image | Throws `DomainException` |
| `AddImage_WithUnsupportedFormat_ShouldThrowDomainException` (`:407`) | `.bmp`-style URL throws | Rewrite as `AddImage_ExtensionlessUrl_ShouldSucceed` — `https://cdn.example.com/img/abc123` is accepted |

New tests: normalized-duplicate rejection (`" …/a.jpg"` vs `"…/a.jpg"`),
case-insensitive duplicate, 11th image rejected, over-500-char URL rejected, non-HTTP
scheme still rejected, returned id matches the added image.

*Effort M — small diff, but it changes established domain behaviour and two tests.*

---

### T2 — EF configuration for `ProductAttribute` and image indexes

**Layer:** Infrastructure · **Effort:** M · **Depends on:** —

Files: `EShop.Catalog.Infrastructure/Data/CatalogDbContext.cs`, new migration under
`EShop.Catalog.Infrastructure/Migrations/`

- Add the missing `modelBuilder.Entity<ProductAttribute>` block (gap #5):
  `ToTable("ProductAttributes")` — plural, consistent with `ProductImages` —
  `Name` required `varchar(100)`, `Value` required `varchar(200)`, index on `ProductId`.
- Add the filtered unique index on `ProductImages (ProductId) WHERE IsMain` (§3).
- Widen `IX_ProductImages_ProductId` to `(ProductId, IsMain, DisplayOrder) INCLUDE (Url)` (§2).
- Optionally expose `DbSet<ProductAttribute>` for symmetry with `ProductImages`.

A table rename plus column tightening is normally risky. Here it is free: audit §4.4
confirms **zero rows exist in either table**, so the migration is pure schema with no
data movement. **This window closes as soon as T5 ships** — do T2 early.

*Effort M — migration review and a rename, but no data risk.*

---

### T3 — Split `ProductDto`; expose images and attributes on detail

**Layer:** Application · **Effort:** M · **Depends on:** T1

New files under `EShop.Catalog.Application/Products/Queries/GetProductsById/`:
`ProductDetailsDto.cs`, `ProductImageDto.cs`, `ProductAttributeDto.cs`
Modified: `Mapping/MappingConfig.cs`, `GetProductByIdQuery.cs`,
`GetProductByIdQueryHandler.cs`, `EShop.Catalog.API/Endpoints/ProductEndpoints.cs`
(the `.Produces<>` on `GET /{id}`)

Adding `Images[]`/`Attributes[]` to the shared `ProductDto` would force the list paths
to return empty arrays, recreating exactly the "documented field that is always null"
problem this plan exists to fix. Split the contract instead (gap #3):

| DTO | Used by | Fields |
|---|---|---|
| `ProductDto` (shape unchanged) | `GetProductsQuery`, `GetProductByCategoryQuery` | existing 11, with a real `MainImageUrl` after T4 |
| `ProductDetailsDto` (new) | `GetProductByIdQuery` | the same 11 + `Images: ProductImageDto[]` + `Attributes: ProductAttributeDto[]` |

- `ProductImageDto`: `Id`, `Url`, `AltText`, `DisplayOrder`, `IsMain`
- `ProductAttributeDto`: `Id`, `Name`, `Value`

Repoint the Mapster config from `Product → ProductDto` to `Product → ProductDetailsDto`,
computing `MainImageUrl` **`IsMain`-first** per §3 — this is the fix for gap #4 — and
mapping both collections in gallery order. Fix the stale `ProductDto` XML comment
("DTO for product in list view", though detail reuses it today).

Closes gap #8 for free: `GetByIdReadOnlyAsync` already eager-loads both collections,
so they stop being fetched-then-discarded.

Additive for clients — existing `GET /{id}` fields are unchanged, two arrays appear.

*Effort M — new contracts plus a mapping rewrite.*

---

### T4 — Populate `MainImageUrl` in both list projections

**Layer:** Infrastructure · **Effort:** S · **Depends on:** T2

Files: `EShop.Catalog.Infrastructure/QueryServices/ProductQueryService.cs`

Replace `MainImageUrl = null` at `:94` (`GetFilteredProductsAsync`) and `:138`
(`GetProductsByCategoryAsync`) with the canonical subquery:

```csharp
MainImageUrl = p.Images
    .OrderByDescending(i => i.IsMain)
    .ThenBy(i => i.DisplayOrder)
    .ThenBy(i => i.CreatedAt)
    .Select(i => i.Url)
    .FirstOrDefault(),
```

Replace the `:79-81` comment with one stating the current decision and the index it
relies on. This is the fix for gap #1.

*Effort S — two lines and a comment, but the single highest-impact change here.*

---

### T5 — Accept images and attributes on product creation

**Layer:** Application + API · **Effort:** M · **Depends on:** T1

Files: `Products/Commands/CreateProduct/CreateProductCommand.cs`,
`CreateProductCommandHandler.cs`, `CreateProductCommandValidator.cs`; new
`CreateProductImageRequest.cs` and `CreateProductAttributeRequest.cs` in the same folder

`CreateProductCommand` gains two optional collections:

```
Images:     [ { Url: string, AltText: string?, DisplayOrder: int } ]
Attributes: [ { Name: string, Value: string } ]
```

Handler: after `Product.Create(...)`, loop `AddImage` / `AddAttribute`. The command is
already `ITransactionalCommand`, so a bad image rolls the whole product back — no
partial products.

Validators — new child validators following the one-`AbstractValidator<T>`-per-type
convention, with `.WithMessage(...)` on every rule:

| Field | Rule |
|---|---|
| `Images` | Max 10 items; no duplicate URLs within the request (case-insensitive, trimmed) |
| `Images[].Url` | NotEmpty; MaxLength 500; absolute `http`/`https`. **No extension check** — extensionless CDN URLs are valid |
| `Images[].AltText` | MaxLength 200 |
| `Images[].DisplayOrder` | `>= 0` |
| `Attributes` | Max 50 items; no duplicate `Name` within the request |
| `Attributes[].Name` | NotEmpty; MaxLength 100 |
| `Attributes[].Value` | NotEmpty; MaxLength 200 |

Cache: `CacheKeysToInvalidate` already declares `products:category:{CategoryId}` — no
change needed.

*Effort M — nested contracts and two child validators.*

---

### T6 — Image sub-resource commands and endpoints

**Layer:** Application + API · **Effort:** L · **Depends on:** T1, T3

New folders under `EShop.Catalog.Application/Products/Commands/`: `AddProductImage/`,
`RemoveProductImage/`, `SetMainProductImage/` — each with Command, Handler, Validator.
Modified: `EShop.Catalog.API/Endpoints/ProductEndpoints.cs`

| Endpoint | Request | Response | Auth |
|---|---|---|---|
| `POST /api/v1/products/{id}/images` | `{ Url, AltText?, DisplayOrder }` | 201 `{ id }` / 400 / 404 | `Admin` |
| `DELETE /api/v1/products/{id}/images/{imageId}` | route params | 204 / 404 | `Admin` |
| `PUT /api/v1/products/{id}/images/{imageId}/main` | route params | 204 / 404 | `Admin` |

All three load via `IProductRepository.GetByIdAsync` — tracked, already includes
`Images` — call the domain method, then `SaveChangesAsync`. All implement
`ITransactionalCommand` and `ICacheInvalidatingCommand`.

**Cache invalidation** follows the existing `UpdateProductCommandHandler` pattern
exactly: declare `product:{ProductId}` on the command, then invalidate
`products:category:{product.CategoryId}` manually in the handler once the aggregate is
loaded (`UpdateProductCommandHandler.cs:44` is the reference).

The `products:list:*` family **cannot** be invalidated (gap #7 — exact-key-only, and
the key embeds every filter/sort/page parameter). Now that `MainImageUrl` is real,
image edits become visible in list results only after the 5-minute TTL. Document this;
do not attempt to work around it here.

Validators: `ProductId`/`ImageId` NotEmpty; `AddProductImageCommand` repeats the
per-image rules from T5.

**Verify before implementing:** the audit did not establish how `DomainException`
surfaces at the API boundary (global handler vs. explicit `Result` mapping). Check this
before deciding whether handlers catch it or let it propagate — `RemoveImage` and
`SetMainImage` both throw on a missing image id, and that must become a 404, not a 500.

Gateway: no change. Audit §6.3 confirms the catch-all write route
(`/api/v1/products/{**catch-all}`, `AuthorizationPolicy: "Admin"`) already covers these.

*Effort L — three full CQRS slices plus endpoints.*

---

### T7 — Add-attribute command and endpoint

**Layer:** Application + API · **Effort:** S · **Depends on:** T1

New: `EShop.Catalog.Application/Products/Commands/AddProductAttribute/` (Command,
Handler, Validator). Modified: `ProductEndpoints.cs`.

`POST /api/v1/products/{id}/attributes` → `{ Name, Value }` → 201 `{ id }` / 400 / 404,
`Admin`. Same cache pattern as T6; validation per T5's attribute rules.

Attributes remain **add-only** — removal and update are out of scope, so gap #11 stays
open by decision.

*Effort S — one slice, mirroring T6's simplest case.*

---

### T8 — Handler and integration tests

**Layer:** Tests · **Effort:** M · **Depends on:** T5, T6, T7

Files: new fixtures under `tests/Services/Catalog/EShop.Catalog.UnitTests/Application/`
and `tests/Services/Catalog/EShop.Catalog.IntegrationTests/Products/`; update
`IntegrationTests/Models/CatalogDtos.cs`

Closes gap #12 — no test currently asserts a `MainImageUrl` value at all.

- **Unit** (Moq + `Assert.That`): each new handler — success, product-not-found,
  image-not-found; `CreateProductCommandHandler` with images and attributes.
- **Mapping** — the first test of `MappingConfig`: assert `MainImageUrl` honours
  `IsMain` over `DisplayOrder`, and that gallery order is `DisplayOrder` then
  `CreatedAt`.
- **Integration** (FluentAssertions + `CatalogApiFactory`): create a product with
  images inline → `GET /` returns a non-null `MainImageUrl`; `GET /{id}` returns
  populated `Images[]`/`Attributes[]`; set-main changes which URL the list returns;
  delete-main promotes the successor; all four write endpoints reject anonymous callers.

Add `Images`/`Attributes` to the test-side response model in `CatalogDtos.cs`.

Caveat: the filtered unique index from T2 cannot be verified here — EF InMemory
(gap #19) does not enforce it.

*Effort M — broad but mechanical.*

---

### T9 — Documentation

**Layer:** Docs · **Effort:** S · **Depends on:** T3, T6, T7

Files: `docs/01-overview/Data Contracts.md`, `docs/05-services/catalog-service.md`,
`docs/09-appendix/catalog-images-audit.md`

- `Data Contracts.md`: document `ProductDetailsDto` and its two arrays, correct the
  `MainImageUrl` entry (gap #13 — it currently implies a value that is always null),
  and add the four new endpoints.
- `catalog-service.md`: add an images/attributes section — it currently has none.
- Audit doc: mark the gaps closed by this work and record #11, #15 (reorder), #17, #18,
  #19 as knowingly still open. Also correct §2.2, which documents the extension
  allowlist as a live constraint on which image hosts are usable — T1 removes it.

*Effort S.*

---

## 5. Execution order

```
T1 ─┬─────────────► T5 ──┐
    │                    ├──► T8 ──► T9
T2 ─┴─► T4               │
    │                    │
    └─► T3 ─► T6 ────────┤
             T7 ─────────┘
```

1. **T1, T2** — parallel; both foundational, and T2's rename is free only until T5 ships
2. **T3, T4** — parallel once T2 lands; T4 alone fixes the highest-impact gap
3. **T5** — inline create
4. **T6, T7** — sub-resource endpoints
5. **T8** — tests
6. **T9** — docs

Useful early checkpoint: **T1 + T2 + T4** alone makes `MainImageUrl` correct for any
data that exists, before any new write path is built.

---

## 6. Definition of Done

- [ ] Admin creates a product with images and attributes in one `POST /api/v1/products`
- [ ] `GET /api/v1/products` returns a real `MainImageUrl` (never unconditionally null)
- [ ] `GET /api/v1/products/{id}` returns populated `Images[]` and `Attributes[]`
- [ ] `MainImageUrl` reflects `IsMain`, not merely the lowest `DisplayOrder`
- [ ] Setting the main image changes what the list endpoint returns
- [ ] Removing the main image promotes a successor; removing the last leaves zero mains
- [ ] Two main images are impossible — domain-guarded and index-guarded
- [ ] `ProductAttributes` table is plural with `varchar(100)`/`varchar(200)`
- [ ] Over-length and malformed URLs return 400, not 500
- [ ] Extensionless `https` CDN URLs are accepted; non-HTTP schemes still rejected
- [ ] All four write endpoints reject anonymous and non-Admin callers
- [ ] `product:` and `products:category:` keys invalidate on every mutation; the
      `products:list:*` staleness window is documented, not silently accepted
- [ ] `dotnet test EShop.slnx` passes, including the two rewritten domain tests
- [ ] Docs updated; remaining open gaps explicitly recorded

---

## 7. Open questions

1. **Attribute name uniqueness.** The domain permits duplicate names; the type comment
   says "for variants (e.g. Size: Large, Color: Blue)", implying one value per name.
   This plan enforces uniqueness only *within a single request* — safe and
   non-breaking. Whether it should become a domain invariant needs a product decision,
   especially since attributes are add-only in Variant A, so duplicates accumulate
   permanently.
2. **Attribute collection cap.** 50 is a guess; no existing convention informs it.
3. **`DiscountPrice`** remains unsettable by any domain method (audit §9). Out of scope
   here, but it is a second field the frontend may expect to be live.

*Resolved during planning:* extensionless URLs — the extension allowlist is removed in
T1 so CDN links are accepted; duplicate URLs now throw rather than silently no-op (T1).

---

## 8. Gaps closed by this plan

| Closed | Deferred by decision | Still open |
|---|---|---|
| #1, #3, #4, #5, #6, #8, #9, #10, #12, #13, #14, #16 | #11 (attribute edit/remove), #15 (reorder) | #7 (list-cache invalidation), #17 (no mutation events), #18 (dead delete event), #19 (InMemory integration tests) |
