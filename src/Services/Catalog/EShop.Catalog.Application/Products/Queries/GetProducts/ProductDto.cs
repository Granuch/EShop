using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// DTO for a product in list results — used by <c>GetProductsQuery</c> and
/// <c>GetProductByCategoryQuery</c>. The single-product detail view (<c>GetProductByIdQuery</c>)
/// returns <see cref="Products.Queries.GetProductsById.ProductDetailsDto"/> instead, which adds
/// the full image gallery and attribute set.
/// </summary>
public record ProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Sku { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? DiscountPrice { get; init; }
    public int StockQuantity { get; init; }
    public ProductStatus Status { get; init; }
    public Guid CategoryId  { get; init; }
    public string? MainImageUrl { get; init; }
    public DateTime CreatedAt { get; init; }
}
