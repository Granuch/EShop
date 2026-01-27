namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// DTO for product in list view
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
    public string Status { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string? MainImageUrl { get; init; }
    public DateTime CreatedAt { get; init; }
}
