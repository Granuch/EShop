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

public record UpdateProductRequest
{
    public Guid ProductId { get; init; }
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
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
}

public record UpdateCategoryRequest
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public record CategoryResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Slug { get; init; } = string.Empty;
    public Guid? ParentCategoryId { get; init; }
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

public record ProblemDetailsResponse
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public int Status { get; init; }
    public string? TraceId { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
}
