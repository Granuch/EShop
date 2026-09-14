using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Basket.Domain.Entities;

/// <summary>
/// Basket item entity
/// </summary>
public class BasketItem : Entity<Guid>
{
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public int Quantity { get; private set; }
    public decimal SubTotal => Price * Quantity;

    private BasketItem() { }

    public BasketItem(Guid productId, string productName, decimal price, int quantity)
    {
        if (productId == Guid.Empty)
            throw new DomainException("Product ID is required.");

        if (string.IsNullOrWhiteSpace(productName))
            throw new DomainException("Product name is required.");

        if (quantity <= 0)
            throw new DomainException("Quantity must be positive.");

        if (price < 0)
            throw new DomainException("Price cannot be negative.");

        Id = Guid.NewGuid();
        ProductId = productId;
        ProductName = productName;
        Price = price;
        Quantity = quantity;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateQuantity(int newQuantity)
    {
        if (newQuantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        Quantity = newQuantity;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Takes the product's current name and price, read from Catalog when the product is added again (Basket audit M1).
    /// </summary>
    public void Refresh(string productName, decimal price)
    {
        if (string.IsNullOrWhiteSpace(productName))
            throw new DomainException("Product name is required.");

        if (price < 0)
            throw new DomainException("Price cannot be negative.");

        if (productName == ProductName && price == Price)
            return;

        ProductName = productName;
        Price = price;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePrice(decimal newPrice)
    {
        if (newPrice < 0)
            throw new DomainException("Price cannot be negative.");

        if (newPrice == Price)
            return;

        Price = newPrice;
        UpdatedAt = DateTime.UtcNow;
    }
}
