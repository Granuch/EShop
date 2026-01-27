using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Basket.Domain.Entities;

/// <summary>
/// Shopping basket aggregate root
/// </summary>
public class ShoppingBasket : AggregateRoot<string>
{
    public string UserId { get; private set; } = string.Empty;

    private readonly List<BasketItem> _items = new();
    public IReadOnlyCollection<BasketItem> Items => _items.AsReadOnly();

    public DateTime LastModifiedAt { get; private set; }

    public decimal TotalPrice => _items.Sum(i => i.SubTotal);
    public int TotalItems => _items.Sum(i => i.Quantity);

    private ShoppingBasket() { }

    public static ShoppingBasket Create(string userId)
    {
        var basket = new ShoppingBasket
        {
            Id = userId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            LastModifiedAt = DateTime.UtcNow
        };

        return basket;
    }

    // TODO: Implement AddItem() with duplicate check
    // public void AddItem(Guid productId, string productName, decimal price, int quantity)

    // TODO: Implement UpdateItemQuantity()
    // public void UpdateItemQuantity(Guid productId, int newQuantity)

    // TODO: Implement RemoveItem()
    // public void RemoveItem(Guid productId)

    // TODO: Implement Clear()
    // public void Clear()

    // TODO: Implement Checkout() that returns BasketCheckedOutEvent
    // public BasketCheckedOutEvent Checkout(string shippingAddress, string paymentMethod)
}
