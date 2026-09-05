namespace EShop.Catalog.Application.Products.Queries.GetProductsById;

/// <summary>
/// DTO for a single product attribute (e.g. Size: Large, Color: Blue), used on the
/// product detail response.
/// </summary>
public record ProductAttributeDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
