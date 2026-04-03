using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Basket.Domain.Events;

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

    public void AddItem(Guid productId, string productName, decimal price, int quantity)
    {
        if (productId == Guid.Empty)
            throw new DomainException("Product ID is required.");

        if (string.IsNullOrWhiteSpace(productName))
            throw new DomainException("Product name is required.");

        if (price < 0)
            throw new DomainException("Price cannot be negative.");

        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
        {
            existingItem.UpdateQuantity(existingItem.Quantity + quantity);
        }
        else
        {
            _items.Add(new BasketItem(productId, productName, price, quantity));
        }

        LastModifiedAt = DateTime.UtcNow;
    }

    public void UpdateItemQuantity(Guid productId, int newQuantity)
    {
        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem == null)
            throw new DomainException($"Product '{productId}' is not in basket.");

        if (newQuantity <= 0)
        {
            _items.Remove(existingItem);
        }
        else
        {
            existingItem.UpdateQuantity(newQuantity);
        }

        LastModifiedAt = DateTime.UtcNow;
    }

    public void RemoveItem(Guid productId)
    {
        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem == null)
            return;

        _items.Remove(existingItem);
        LastModifiedAt = DateTime.UtcNow;
    }

    public void Clear()
    {
        _items.Clear();
        LastModifiedAt = DateTime.UtcNow;
    }

    public void ApplyPriceChange(Guid productId, decimal newPrice)
    {
        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem == null)
            return;

        existingItem.UpdatePrice(newPrice);
        LastModifiedAt = DateTime.UtcNow;
    }

    public void Checkout(string shippingAddress, string paymentMethod)
    {
        if (_items.Count == 0)
            throw new DomainException("Cannot checkout an empty basket.");

        if (string.IsNullOrWhiteSpace(shippingAddress))
            throw new DomainException("Shipping address is required.");

        if (string.IsNullOrWhiteSpace(paymentMethod))
            throw new DomainException("Payment method is required.");

        AddDomainEvent(new BasketCheckedOutDomainEvent
        {
            UserId = UserId,
            Items = _items
                .Select(item => new BasketCheckedOutDomainEventItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Price = item.Price,
                    Quantity = item.Quantity
                })
                .ToList(),
            TotalPrice = TotalPrice,
            ShippingAddress = shippingAddress,
            PaymentMethod = paymentMethod
        });

        LastModifiedAt = DateTime.UtcNow;
    }
}
