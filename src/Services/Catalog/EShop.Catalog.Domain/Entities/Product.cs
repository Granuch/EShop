using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Events;

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
    
    public static Product Create(string name, string sku, decimal price, int stockQuantity, Guid categoryId)
    {
        
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name is required.");

        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU is required.");

        if (price <= 0)
            throw new ArgumentException("Price must be greater than zero.");

        if (stockQuantity < 0)
            throw new ArgumentException("Stock quantity cannot be negative.");
        
        var product = new Product
        {
            CategoryId =  categoryId,
            Name = name,
            Sku = sku,
            Price = price,
            StockQuantity = stockQuantity,
        };

        product.AddDomainEvent(new ProductCreatedEvent
        {
            ProductId = product.Id,
            ProductName = product.Name,
            Price = product.Price,
        });
        
        return product;
    }
    
    public void UpdatePrice(decimal newPrice)
    {
        if (newPrice <= 0)
        {
            throw new ArgumentException("Price must be greater than zero");
        }

        if (newPrice == Price)
        {
            return;
        }
        var oldPrice = this.Price;
        
        AddDomainEvent(new ProductPriceChangedEvent
        {
            NewPrice =  newPrice,
            OldPrice = oldPrice,
            ProductId = Id
        });
        
        Price = newPrice;
    }

    
    public void UpdateStock(int quantity)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Product is deleted.");
        
        if (quantity < 0)
            throw new ArgumentException("Stock quantity cannot be negative");
        
        if (quantity == StockQuantity)
            return;

        if (quantity == 0)
        {
            AddDomainEvent(new ProductOutOfStockEvent
            {
                ProductId = Id,
            });
            StockQuantity = quantity;
        }

        if (StockQuantity == 0)
        {
            AddDomainEvent(new ProductBackInStockEvent
            {
                ProductId = Id,
            });
            StockQuantity = quantity;
        }
        
        StockQuantity = quantity;
    }
    
    public void Publish()
    {
        if (IsDeleted)
            throw new InvalidOperationException("Product is deleted.");
        
        if (Status == ProductStatus.Draft)
            Status = ProductStatus.Active;
        else
            throw new InvalidOperationException("Cannot publish a non-draft product");
    }

    // For now i dont use deleted by as no field to store it exist, may add later
    public void SoftDelete()
    {
        if (IsDeleted)
            return;
        
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        Status = ProductStatus.Discontinued;
    }
    
    public void AddImage(string url, string? altText, int displayOrder)
    {
        if(IsDeleted)
            throw new InvalidOperationException("Product is deleted.");
        
        if(_images.Any(x => x.Url == url))
            return;
        
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Product url cannot be null or empty.");
        
        if (displayOrder < 0)
            throw new ArgumentException("Display order cannot be negative.");
        
        var newImage = new ProductImage(Id ,url, altText, displayOrder);
        _images.Add(newImage);
    }

    public void RemoveImage(Guid imageId)
    {
        if(IsDeleted)
            throw new InvalidOperationException("Product is deleted.");
        
        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image == null)
            throw new ArgumentException("Product image not found.");
        
        _images.Remove(image);

    }
    
    // Attributes with same name might be an issue not sure yet
    public void AddAttribute(string name, string value)
    {
        if (IsDeleted)
            throw new InvalidOperationException("Product is deleted.");
        
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Attribute name cannot be null or empty.");
        
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Attribute value cannot be null or empty.");
        
        var  newAttribute = new ProductAttribute(Id , name, value);
        _attributes.Add(newAttribute);
    }
}

public enum ProductStatus
{
    Draft,
    Active,
    Discontinued
}
