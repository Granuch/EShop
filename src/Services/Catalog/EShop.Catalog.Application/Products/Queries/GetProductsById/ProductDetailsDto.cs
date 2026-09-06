using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.Application.Products.Queries.GetProductsById;

/// <summary>
/// DTO for a single product's detail view: the same fields as <see cref="Products.Queries.GetProducts.ProductDto"/>,
/// plus the full image gallery and attribute set.
/// </summary>
public record ProductDetailsDto
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
    public IReadOnlyCollection<ProductImageDto> Images { get; init; } = Array.Empty<ProductImageDto>();
    public IReadOnlyCollection<ProductAttributeDto> Attributes { get; init; } = Array.Empty<ProductAttributeDto>();
}
