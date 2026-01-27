using EShop.BuildingBlocks.Domain;

namespace EShop.Catalog.Domain.Entities;

/// <summary>
/// Product image entity
/// </summary>
public class ProductImage : Entity<Guid>
{
    public Guid ProductId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string? AltText { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsMain { get; private set; }

    private ProductImage() { }

    public ProductImage(Guid productId, string url, string? altText, int displayOrder)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        Url = url;
        AltText = altText;
        DisplayOrder = displayOrder;
        CreatedAt = DateTime.UtcNow;
    }

    // TODO: Implement SetAsMain() method
    // TODO: Add image size/format validation
}
