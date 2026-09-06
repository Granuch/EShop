namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// A single image supplied inline when creating a product.
/// </summary>
public record CreateProductImageRequest
{
    public string Url { get; init; } = string.Empty;
    public string? AltText { get; init; }
    public int DisplayOrder { get; init; }
}
