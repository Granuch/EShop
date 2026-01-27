using EShop.BuildingBlocks.Domain;

namespace EShop.Catalog.Domain.Entities;

/// <summary>
/// Product aggregate root
/// </summary>
public class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public decimal? DiscountPrice { get; private set; }
    public int StockQuantity { get; private set; }
    public ProductStatus Status { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;

    private readonly List<ProductImage> _images = new();
    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();

    private readonly List<ProductAttribute> _attributes = new();
    public IReadOnlyCollection<ProductAttribute> Attributes => _attributes.AsReadOnly();

    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    private Product() { }

    // TODO: Implement factory method Create() with validation
    // public static Product Create(string name, string sku, decimal price, int stockQuantity, Guid categoryId, string createdBy)

    // TODO: Implement UpdatePrice() with PriceChangedEvent
    // public void UpdatePrice(decimal newPrice, string updatedBy)

    // TODO: Implement UpdateStock() with OutOfStockEvent/BackInStockEvent
    // public void UpdateStock(int quantity, string updatedBy)

    // TODO: Implement Publish() to change status from Draft to Active
    // public void Publish()

    // TODO: Implement SoftDelete()
    // public void SoftDelete(string deletedBy)

    // TODO: Implement AddImage() and RemoveImage()
    // public void AddImage(string url, string? altText, int displayOrder)

    // TODO: Implement AddAttribute() for product variants
    // public void AddAttribute(string name, string value)
}

public enum ProductStatus
{
    Draft,
    Active,
    Discontinued
}
