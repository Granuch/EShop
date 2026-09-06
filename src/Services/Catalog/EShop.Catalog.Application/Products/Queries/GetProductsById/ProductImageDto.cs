namespace EShop.Catalog.Application.Products.Queries.GetProductsById;

/// <summary>
/// DTO for a single product image, used on the product detail response.
/// </summary>
public record ProductImageDto
{
    public Guid Id { get; init; }
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsMain { get; init; }
}
