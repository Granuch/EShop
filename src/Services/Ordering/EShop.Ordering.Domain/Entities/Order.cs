using System.Globalization;
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

        // AddItem already refused a second line for the same product; Create did not, so the same
        // invariant held or not depending on how the order was built.
        if (itemList.Select(i => i.ProductId).Distinct().Count() != itemList.Count)
            throw new DomainException("Each product may appear only once in an order.");

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
            TotalAmount = order.TotalPrice,
            Items = order._items.Select(i => new OrderCreatedLine
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity
            }).ToList()
        });

        return order;
    }

    public void AddItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        EnsurePending("Items can only be changed while the order is pending");

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
        EnsurePending("Items can only be changed while the order is pending");

        if (_items.Count == 1)
            throw new DomainException("Order must have at least one item.");

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new DomainException("Order item not found.");

        _items.Remove(item);

        RecalculateTotal();
    }

    /// <summary>
    /// Records a successful payment. <paramref name="paidAmount"/> must equal <see cref="TotalPrice"/>:
    /// Payment charges the total it was sent when the order was created, so a mismatch means the items
    /// changed after the charge, or the charge was wrong — and marking the order paid anyway would
    /// release goods nobody paid for. Both figures are compared at cent precision, rounding half away
    /// from zero, because that is what the <c>numeric(18,2)</c> column does to the stored total.
    /// </summary>
    public void MarkAsPaid(string paymentIntentId, decimal paidAmount)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException("Only pending orders can be marked as paid.");

        if (string.IsNullOrWhiteSpace(paymentIntentId))
            throw new DomainException("Payment intent ID is required.");

        if (ToCents(paidAmount) != ToCents(TotalPrice))
            throw new DomainException(
                $"Paid amount {ToCents(paidAmount).ToString("0.00", CultureInfo.InvariantCulture)} does not match "
                + $"the order total {ToCents(TotalPrice).ToString("0.00", CultureInfo.InvariantCulture)}.");

        Status = OrderStatus.Paid;
        PaymentIntentId = paymentIntentId;
        PaidAt = DateTime.UtcNow;

        AddDomainEvent(new OrderPaidDomainEvent
        {
            OrderId = Id,
            UserId = UserId,
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

    /// <summary>
    /// Only a pending order can be cancelled. A paid order used to be cancellable too, but nothing
    /// refunds the payment, so cancelling it kept the customer's money against an order that no longer
    /// existed. Payment consumes <c>OrderCancelledEvent</c> since Ordering audit Stage 9, but only to
    /// cancel a payment that has not been captured — it never refunds.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Cancelled)
            throw new DomainException("Order is already cancelled.");

        EnsurePending("Only pending orders can be cancelled");

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

    /// <summary>
    /// Records that Payment refunded this order's payment in full (Ordering audit Stage 11, driven by
    /// <c>PaymentRefundedEvent</c>). Refunded is final: nothing moves an order out of it.
    ///
    /// <para>
    /// Pending is allowed on purpose. A payment can succeed, and be refunded, before Ordering has processed
    /// its success; left Pending, the order would be marked Paid by that late success and could then ship.
    /// A cancelled order is refused: it is already final, and its refund only settles the payment side.
    /// </para>
    /// </summary>
    public void Refund()
    {
        if (Status == OrderStatus.Refunded)
            throw new DomainException("Order is already refunded.");

        if (Status == OrderStatus.Cancelled)
            throw new DomainException("A cancelled order cannot be refunded; its payment is settled in Payment.");

        Status = OrderStatus.Refunded;
    }

    private void RecalculateTotal()
    {
        TotalPrice = _items.Sum(i => i.SubTotal);
    }

    /// <summary>
    /// Pending is the only state in which the total may still move. Once paid, the total is what the
    /// customer was charged, so changing items afterwards made the order disagree with its payment.
    /// </summary>
    private void EnsurePending(string rule)
    {
        if (Status != OrderStatus.Pending)
            throw new DomainException($"{rule}; this order is already {Status.ToString().ToLowerInvariant()}.");
    }

    private static decimal ToCents(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
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
