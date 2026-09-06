namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// A single key/value attribute supplied inline when creating a product.
/// </summary>
public record CreateProductAttributeRequest
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
