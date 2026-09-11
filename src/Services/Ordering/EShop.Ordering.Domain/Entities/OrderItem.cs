using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Ordering.Domain.Entities;

/// <summary>
/// Order item entity - captures a price snapshot at order time
/// </summary>
public class OrderItem : Entity<Guid>
{
    /// <summary>The <c>ProductName</c> column's length; Catalog caps product names at the same 200.</summary>
    public const int MaxProductNameLength = 200;

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal SubTotal => UnitPrice * Quantity;

    private OrderItem() { }

    /// <exception cref="DomainException">
    /// Audit M1: these were ArgumentExceptions, which nothing maps, so they surfaced as 500. The name
    /// length was not checked at all and failed at the database instead (Postgres 22001, also a 500).
    /// </exception>
    public OrderItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be positive.");
        if (unitPrice < 0)
            throw new DomainException("Price cannot be negative.");
        if (string.IsNullOrWhiteSpace(productName))
            throw new DomainException("Product name is required.");
        if (productName.Length > MaxProductNameLength)
            throw new DomainException($"Product name must not exceed {MaxProductNameLength} characters.");

        Id = Guid.NewGuid();
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        CreatedAt = DateTime.UtcNow;
    }
}
