using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.IntegrationTests.Models;

/// <summary>
/// DTOs for Catalog API requests/responses in tests
/// </summary>

public record CreateProductRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public Guid CategoryId { get; init; }
    public List<CreateProductImageRequestDto>? Images { get; init; }
    public List<CreateProductAttributeRequestDto>? Attributes { get; init; }
}

public record CreateProductImageRequestDto
{
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public int DisplayOrder { get; init; }
}

public record CreateProductAttributeRequestDto
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public record AddProductImageRequest
{
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public int DisplayOrder { get; init; }
}

public record AddProductAttributeRequest
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

// Admin panel S3. Full-replacement PUT semantics, unlike UpdateProductRequest above: Url is
// required and AltText is replaced outright, because these endpoints are new and have no existing
// caller sending a partial body to keep working.
public record UpdateProductImageRequest
{
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
}

public record ReorderProductImagesRequest
{
    public IReadOnlyList<Guid>? ImageIds { get; init; }
}

public record UpdateProductAttributeRequest
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public record ReplaceProductAttributesRequest
{
    public IReadOnlyList<ReplaceProductAttributeItem>? Attributes { get; init; }
}

public record ReplaceProductAttributeItem
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

// Admin panel S4. Exactly one of Delta and Absolute is sent; both nullable so a request can omit
// the one it does not mean, which is what the command's exactly-one rule checks.
public record AdjustProductStockRequest
{
    public int? Delta { get; init; }
    public int? Absolute { get; init; }
    public string? Reason { get; init; }
}

public record ProductStockResponse
{
    public Guid ProductId { get; init; }
    public int StockQuantity { get; init; }
}

// Admin panel S5.
public record MoveCategoryRequest
{
    public Guid? NewParentCategoryId { get; init; }
}

public record ReorderCategoriesRequest
{
    public Guid? ParentCategoryId { get; init; }
    public IReadOnlyList<Guid>? CategoryIds { get; init; }
}

public record CategoryStatsResponse
{
    public Guid CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public int ProductCount { get; init; }
    public int PublishedProductCount { get; init; }
    public int TotalStock { get; init; }
    public int OutOfStockCount { get; init; }
    public int ChildCategoryCount { get; init; }
}

public record UpdateProductRequest
{
    public Guid ProductId { get; init; }
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }

    // Admin panel S2. All four are optional — omitted means "leave it alone" — so the
    // price-and-stock requests this endpoint has always accepted keep working unchanged.
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Sku { get; init; }
    public Guid? CategoryId { get; init; }
}

/// <summary>
/// Body of PUT /api/v1/products/{id}/discount. The route owns the product id, so the body carries
/// only the price — the endpoint overwrites ProductId after binding.
/// </summary>
public record SetProductDiscountRequest
{
    public decimal DiscountPrice { get; init; }
}

public record ProductResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? DiscountPrice { get; init; }
    public int StockQuantity { get; init; }
    public ProductStatus Status { get; init; }
    public Guid CategoryId { get; init; }
    public string? MainImageUrl { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Response shape of GET /api/v1/products/{id} — the list DTO's fields plus the image
/// gallery and attribute set. Mirrors ProductDetailsDto on the API side.
/// </summary>
public record ProductDetailsResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? DiscountPrice { get; init; }
    public int StockQuantity { get; init; }
    public ProductStatus Status { get; init; }
    public Guid CategoryId { get; init; }
    public string? MainImageUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<ProductImageResponse> Images { get; init; } = [];
    public List<ProductAttributeResponse> Attributes { get; init; } = [];
}

public record ProductImageResponse
{
    public Guid Id { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsMain { get; init; }
}

public record ProductAttributeResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public record CreateCategoryRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public Guid? ParentCategoryId { get; init; }
    public string? Description { get; init; }
    public int? DisplayOrder { get; init; }
}

public record UpdateCategoryRequest
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Null (omitted) leaves the stored description; "" clears it (Stage 8, M10).</summary>
    public string? Description { get; init; }
    public int? DisplayOrder { get; init; }
}

public record CategoryResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Slug { get; init; } = string.Empty;
    public Guid? ParentCategoryId { get; init; }
    public string? ParentCategoryName { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    public List<CategoryResponse>? ChildCategories { get; init; }
}

public record CreatedResponse
{
    public Guid Id { get; init; }
}

public record PagedResponse<T>
{
    public IEnumerable<T> Items { get; init; } = Enumerable.Empty<T>();
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public bool HasPreviousPage { get; init; }
    public bool HasNextPage { get; init; }
}

public record CursorPagedResponse<T>
{
    public IEnumerable<T> Items { get; init; } = Enumerable.Empty<T>();
    public int PageSize { get; init; }
    public string? NextCursor { get; init; }
    public string? PreviousCursor { get; init; }
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage { get; init; }
}

public record ProblemDetailsResponse
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public int Status { get; init; }

    /// <summary>
    /// The machine-readable discriminator. Previously these assertions read <see cref="Title"/>,
    /// which under RFC 7807 is a human-readable summary of the status, not an error code.
    /// </summary>
    public string? ErrorCode { get; init; }

    public string? TraceId { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
}

// ---------- Admin panel S16: bulk actions, import ----------

public record BulkProductIdsRequest
{
    public List<Guid> ProductIds { get; init; } = [];
}

public record BulkChangeCategoryRequest
{
    public List<Guid> ProductIds { get; init; } = [];
    public Guid CategoryId { get; init; }
}

public record BulkPriceRequest
{
    public List<BulkPriceItem> Items { get; init; } = [];
}

public record BulkPriceItem
{
    public Guid ProductId { get; init; }
    public decimal Price { get; init; }
}

public record BulkReportResponse
{
    public int Requested { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public List<BulkItemResponse> Items { get; init; } = [];
}

public record BulkItemResponse
{
    public Guid ProductId { get; init; }
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
}

public record ImportProductsRequest
{
    public List<ImportProductRowRequest> Products { get; init; } = [];
}

public record ImportProductRowRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public Guid CategoryId { get; init; }
}

public record ImportReportResponse
{
    public int Requested { get; init; }
    public int Created { get; init; }
    public int Failed { get; init; }
    public List<ImportRowResponse> Rows { get; init; } = [];
}

public record ImportRowResponse
{
    public int Index { get; init; }
    public string? Sku { get; init; }
    public Guid? ProductId { get; init; }
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
}
