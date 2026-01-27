using EShop.BuildingBlocks.Domain;
using EShop.Ordering.Domain.ValueObjects;

namespace EShop.Ordering.Domain.Entities;

/// <summary>
/// Order aggregate root following DDD patterns
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

    // TODO: Implement factory method Create()
    // public static Order Create(string userId, Address shippingAddress, IEnumerable<OrderItem> items)

    // TODO: Implement MarkAsPaid()
    // public void MarkAsPaid(string paymentIntentId)

    // TODO: Implement Ship()
    // public void Ship()

    // TODO: Implement Deliver()
    // public void Deliver()

    // TODO: Implement Cancel()
    // public void Cancel(string reason)

    // TODO: Add domain events for each state transition
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
