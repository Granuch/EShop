using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Domain.Entities;

/// <summary>
/// Order item entity - captures a price snapshot at order time
/// </summary>
public class OrderItem : Entity<Guid>
{
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal SubTotal => UnitPrice * Quantity;

    private OrderItem() { }

    public OrderItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        if (unitPrice < 0)
            throw new ArgumentException("Price cannot be negative", nameof(unitPrice));
        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentException("Product name is required", nameof(productName));

        Id = Guid.NewGuid();
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        CreatedAt = DateTime.UtcNow;
    }
}
