using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;

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
        if (productId == Guid.Empty)
            throw new DomainException("Product attribute requires a valid product id.");

        var normalizedName = NormalizeName(name);
        var normalizedValue = NormalizeValue(value);

        Id = Guid.NewGuid();
        ProductId = productId;
        Name = normalizedName;
        Value = normalizedValue;
        CreatedAt = DateTime.UtcNow;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Attribute name cannot be empty.");

        var normalizedName = name.Trim();
        if (normalizedName.Length > 100)
            throw new DomainException("Attribute name must be 100 characters or fewer.");

        return normalizedName;
    }

    private static string NormalizeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Attribute value cannot be empty.");

        var normalizedValue = value.Trim();
        if (normalizedValue.Length > 200)
            throw new DomainException("Attribute value must be 200 characters or fewer.");

        return normalizedValue;
    }
}
