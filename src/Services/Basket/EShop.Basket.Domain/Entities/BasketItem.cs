using EShop.BuildingBlocks.Domain;

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
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        Id = Guid.NewGuid();
        ProductId = productId;
        ProductName = productName;
        Price = price;
        Quantity = quantity;
        CreatedAt = DateTime.UtcNow;
    }

    // TODO: Implement UpdateQuantity()
    // public void UpdateQuantity(int newQuantity)

    // TODO: Implement UpdatePrice() for price synchronization from catalog
    // public void UpdatePrice(decimal newPrice)
}
