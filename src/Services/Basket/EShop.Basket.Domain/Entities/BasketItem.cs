using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Basket.Domain.Entities;

/// <summary>
/// Basket item entity. A basket holds one line per product, so the product id is the line's identity (Basket audit L1,
/// S9): <see cref="Entity{TId}.Id"/> used to be a new Guid, and <see cref="Entity{TId}.CreatedAt"/> a new time, every
/// time the basket was read.
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
        : this(productId, productName, price, quantity, DateTime.UtcNow)
    {
    }

    private BasketItem(Guid productId, string productName, decimal price, int quantity, DateTime addedAt)
    {
        if (productId == Guid.Empty)
            throw new DomainException("Product ID is required.");

        if (string.IsNullOrWhiteSpace(productName))
            throw new DomainException("Product name is required.");

        if (quantity <= 0)
            throw new DomainException("Quantity must be positive.");

        if (price < 0)
            throw new DomainException("Price cannot be negative.");

        Id = productId;
        ProductId = productId;
        ProductName = productName;
        Price = price;
        Quantity = quantity;
        CreatedAt = addedAt;
    }

    /// <summary>A stored line, as it was stored. The per-line limit is not applied: it is checked on changes only.</summary>
    internal static BasketItem Rehydrate(StoredBasketItem stored, DateTime addedAtIfUnknown)
        => new(stored.ProductId, stored.ProductName, stored.Price, stored.Quantity, stored.AddedAt ?? addedAtIfUnknown);

    public void UpdateQuantity(int newQuantity)
    {
        if (newQuantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        if (newQuantity > ShoppingBasket.MaxQuantityPerLine)
            throw new DomainException($"A basket line can hold at most {ShoppingBasket.MaxQuantityPerLine} of a product.");

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
