using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
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
            throw new DomainException("Product name is required.");

        if (string.IsNullOrWhiteSpace(sku))
            throw new DomainException("SKU is required.");

        if (price <= 0)
            throw new DomainException("Price must be greater than zero.");

        if (stockQuantity < 0)
            throw new DomainException("Stock quantity cannot be negative.");
        
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            Name = name,
            Sku = sku,
            Price = price,
            StockQuantity = stockQuantity,
            Status = ProductStatus.Draft
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
            throw new DomainException("Price must be greater than zero.");
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
            throw new DomainException("Cannot update stock of a deleted product.");

        if (quantity < 0)
            throw new DomainException("Stock quantity cannot be negative.");

        if (quantity == StockQuantity)
            return;

        var previousQuantity = StockQuantity;
        StockQuantity = quantity;

        if (quantity == 0 && previousQuantity > 0)
        {
            AddDomainEvent(new ProductOutOfStockEvent
            {
                ProductId = Id,
            });
        }
        else if (previousQuantity == 0 && quantity > 0)
        {
            AddDomainEvent(new ProductBackInStockEvent
            {
                ProductId = Id,
            });
        }
    }
    
    public void Publish()
    {
        if (IsDeleted)
            throw new DomainException("Cannot publish a deleted product.");

        if (Status != ProductStatus.Draft)
            throw new DomainException("Cannot publish a non-draft product.");

        Status = ProductStatus.Active;
    }

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
        if (IsDeleted)
            throw new DomainException("Cannot add image to a deleted product.");

        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("Product image URL cannot be empty.");

        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        if (_images.Any(x => x.Url == url))
            return;
        
        var newImage = new ProductImage(Id ,url, altText, displayOrder);
        _images.Add(newImage);
    }

    public void RemoveImage(Guid imageId)
    {
        if (IsDeleted)
            throw new DomainException("Cannot remove image from a deleted product.");

        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image == null)
            throw new DomainException("Product image not found.");

        _images.Remove(image);
    }
    
    public void AddAttribute(string name, string value)
    {
        if (IsDeleted)
            throw new DomainException("Cannot add attribute to a deleted product.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Attribute name cannot be empty.");

        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("Attribute value cannot be empty.");
        
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
