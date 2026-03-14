using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using EShop.Ordering.Domain.Events;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.Domain.Entities;

/// <summary>
/// Order aggregate root following DDD patterns.
/// Enforces invariants: must have items, valid quantities, and correct state transitions.
/// </summary>
public class Order : AggregateRoot<Guid>
{
    public string UserId { get; private set; } = string.Empty;
    public Address ShippingAddress { get; private set; } = null!;
    public decimal TotalPrice { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? PaymentIntentId { get; private set; }

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public DateTime? PaidAt { get; private set; }
    public DateTime? ShippedAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    private Order() { }

    public static Order Create(string userId, Address shippingAddress, IEnumerable<OrderItem> items)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("User ID is required.");

        ArgumentNullException.ThrowIfNull(shippingAddress);

        var itemList = items?.ToList() ?? throw new DomainException("Order must have at least one item.");
        if (itemList.Count == 0)
            throw new DomainException("Order must have at least one item.");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ShippingAddress = shippingAddress,
            Status = OrderStatus.Pending
        };

        foreach (var item in itemList)
        {
            order._items.Add(item);
        }

        order.RecalculateTotal();

        order.AddDomainEvent(new OrderCreatedDomainEvent
        {
            OrderId = order.Id,
            UserId = order.UserId,
            TotalAmount = order.TotalPrice
        });

        return order;
    }

    public void AddItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        EnsureNotCompleted();

        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        if (unitPrice < 0)
            throw new DomainException("Unit price cannot be negative.");

        var existingItem = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existingItem != null)
            throw new DomainException($"Product '{productName}' already exists in this order.");

        var item = new OrderItem(productId, productName, unitPrice, quantity);
        _items.Add(item);
        RecalculateTotal();
    }

    public void RemoveItem(Guid itemId)
    {
        EnsureNotCompleted();

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new DomainException("Order item not found.");

        _items.Remove(item);

        if (_items.Count == 0)
            throw new DomainException("Order must have at least one item.");

        RecalculateTotal();
    }

    public void MarkAsPaid(string paymentIntentId)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Only pending orders can be marked as paid.");

        if (string.IsNullOrWhiteSpace(paymentIntentId))
            throw new DomainException("Payment intent ID is required.");

        Status = OrderStatus.Paid;
        PaymentIntentId = paymentIntentId;
        PaidAt = DateTime.UtcNow;

        AddDomainEvent(new OrderPaidDomainEvent
        {
            OrderId = Id,
            PaymentIntentId = paymentIntentId
        });
    }

    public void Ship()
    {
        if (Status != OrderStatus.Paid)
            throw new DomainException("Only paid orders can be shipped.");

        Status = OrderStatus.Shipped;
        ShippedAt = DateTime.UtcNow;

        AddDomainEvent(new OrderShippedDomainEvent
        {
            OrderId = Id,
            UserId = UserId
        });
    }

    public void Deliver()
    {
        if (Status != OrderStatus.Shipped)
            throw new DomainException("Only shipped orders can be delivered.");

        Status = OrderStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Shipped || Status == OrderStatus.Delivered)
            throw new DomainException("Cannot cancel a shipped or delivered order.");

        if (Status == OrderStatus.Cancelled)
            throw new DomainException("Order is already cancelled.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Cancellation reason is required.");

        Status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = reason;

        AddDomainEvent(new OrderCancelledDomainEvent
        {
            OrderId = Id,
            UserId = UserId,
            Reason = reason
        });
    }

    private void RecalculateTotal()
    {
        TotalPrice = _items.Sum(i => i.SubTotal);
    }

    private void EnsureNotCompleted()
    {
        if (Status is OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled)
            throw new DomainException("Cannot modify an order that is shipped, delivered, or cancelled.");
    }
}

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Delivered,
    Cancelled,
    Refunded
}
