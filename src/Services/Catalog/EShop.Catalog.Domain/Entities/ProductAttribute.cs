using EShop.BuildingBlocks.Domain;

namespace EShop.Catalog.Domain.Entities;

/// <summary>
/// Product attribute for variants (e.g., Size: Large, Color: Blue)
/// </summary>
public class ProductAttribute : Entity<Guid>
{
    public Guid ProductId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;

    private ProductAttribute() { }

    public ProductAttribute(Guid productId, string name, string value)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        Name = name;
        Value = value;
        CreatedAt = DateTime.UtcNow;
    }

    // TODO: Add attribute validation (e.g., Size must be S/M/L/XL)
}
