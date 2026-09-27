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
    /// <summary>
    /// The currency every order is priced in: Ordering has no per-order currency, and its consumers refuse any other.
    /// Published on <c>OrderCreatedEvent</c> since Notification audit S7 (D11), so the confirmation email can say it.
    /// </summary>
    public const string PricingCurrency = "USD";

    public string UserId { get; private set; } = string.Empty;
    public Address ShippingAddress { get; private set; } = null!;
    public decimal TotalPrice { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? PaymentIntentId { get; private set; }

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private readonly List<OrderStatusHistory> _statusHistory = new();

    /// <summary>
    /// The transitions recorded on this instance (Admin panel S9).
    ///
    /// <para>
    /// <b>Write-side only, and empty on a loaded order.</b> <c>IOrderRepository.GetByIdAsync</c>
    /// includes the items and nothing else, so on an order read back from the database this collection
    /// contains only the rows the current operation appended — never the order's history. Reading it to
    /// count, validate or display anything is therefore wrong in a way that fails silently; the read
    /// path is <c>IOrderQueryService.GetOrderStatusHistoryAsync</c>, which projects from the table.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<OrderStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    private readonly List<OrderNote> _notes = new();

    /// <inheritdoc cref="StatusHistory"/>
    public IReadOnlyCollection<OrderNote> Notes => _notes.AsReadOnly();

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

        EnsureStorable(itemList.Sum(i => i.SubTotal));

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

        // The timeline starts here. Without this row the history of an order that never moved is
        // empty, which reads as "no data" rather than "created, still pending".
        order.RecordTransition(null, OrderStatus.Pending);

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
        EnsureStorable(TotalPrice + item.SubTotal);
        var previousTotal = TotalPrice;
        _items.Add(item);
        RecalculateTotal();
        RaiseTotalChangedIfMoved(previousTotal);
    }

    public void RemoveItem(Guid itemId)
    {
        EnsurePending("Items can only be changed while the order is pending");

        if (_items.Count == 1)
            throw new DomainException("Order must have at least one item.");

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new DomainException("Order item not found.");

        var previousTotal = TotalPrice;
        _items.Remove(item);

        RecalculateTotal();
        RaiseTotalChangedIfMoved(previousTotal);
    }

    /// <summary>
    /// Changes an existing line's quantity, in place (Admin panel S8). Delete-and-re-add would work
    /// against <c>RemoveItem</c>'s "an order must have at least one item" guard for a single-line
    /// order, and would mint a new <c>OrderItem.Id</c> for a line the client is still addressing.
    ///
    /// <para>
    /// Same Pending-only rule as <see cref="AddItem"/> and <see cref="RemoveItem"/>: the quantity is
    /// an input to <see cref="TotalPrice"/>, and once paid the total is what the customer was charged.
    /// Setting the quantity it already has is a no-op rather than an error.
    /// </para>
    /// </summary>
    /// <exception cref="DomainException">
    /// The order is not pending, the quantity is not positive, the item is not on this order, or the
    /// resulting total would exceed <see cref="MaxTotal"/>.
    /// </exception>
    public void UpdateItemQuantity(Guid itemId, int quantity)
    {
        EnsurePending("Items can only be changed while the order is pending");

        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new DomainException("Order item not found.");

        // Checked against the total the change WOULD produce, before the change, so a refused line
        // leaves the order exactly as it was — the same ordering AddItem uses.
        EnsureStorable(TotalPrice - item.SubTotal + (item.UnitPrice * quantity));

        var previousTotal = TotalPrice;
        item.ChangeQuantity(quantity);
        RecalculateTotal();
        RaiseTotalChangedIfMoved(previousTotal);
    }

    /// <summary>
    /// Changes where the order ships to (Admin panel S8) — a customer who moved, or an admin fixing a
    /// typo before the parcel leaves.
    ///
    /// <para>
    /// Allowed while Pending <b>or</b> Paid, refused from Shipped onward: once a shipment exists the
    /// address on the order no longer decides where the goods go, so accepting the edit would report
    /// a delivery address the parcel is not travelling to. Cancelled and Refunded are refused for the
    /// same reason in reverse — they are final, and nothing will be delivered.
    /// </para>
    /// </summary>
    /// <exception cref="DomainException">The order is past Paid.</exception>
    public void UpdateShippingAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (Status is not (OrderStatus.Pending or OrderStatus.Paid))
            throw new DomainException(
                "The shipping address can only be changed before the order ships; "
                + $"this order is {Status.ToString().ToLowerInvariant()}.");

        ShippingAddress = address;
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

        var previous = Status;
        Status = OrderStatus.Paid;
        PaymentIntentId = paymentIntentId;
        PaidAt = DateTime.UtcNow;
        RecordTransition(previous, Status);

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

        var previous = Status;
        Status = OrderStatus.Shipped;
        ShippedAt = DateTime.UtcNow;
        RecordTransition(previous, Status);

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

        var previous = Status;
        Status = OrderStatus.Delivered;
        DeliveredAt = DateTime.UtcNow;
        RecordTransition(previous, Status);
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

        var previous = Status;
        Status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = reason;
        RecordTransition(previous, Status, reason);

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

        var previous = Status;
        Status = OrderStatus.Refunded;
        RecordTransition(previous, Status);
    }

    /// <summary>
    /// The largest number of notes one order may carry (Admin panel S9).
    ///
    /// <para>
    /// The number lives here because it is a rule about an order, but <b>it is enforced by the handler,
    /// not by this aggregate, and that is deliberate</b>: <c>_notes</c> is not loaded by
    /// <c>GetByIdAsync</c>, so a cap written as <c>if (_notes.Count >= MaxNotes)</c> would compare
    /// against zero on every persisted order and never fire. A pre-check with a <c>COUNT</c> query is
    /// the only place that can see the real number; it can be beaten by two concurrent writes, the same
    /// way Catalog's SKU pre-check can, and that is accepted — the cap exists to bound an unpaged read,
    /// not to hold an invariant.
    /// </para>
    /// </summary>
    public const int MaxNotes = 500;

    /// <summary>
    /// Attaches an internal note (Admin panel S9, endpoint #60). Allowed in every status, including the
    /// final ones: most of what is worth writing down about an order — a chargeback, a complaint, a
    /// goodwill credit — happens after it is delivered or cancelled.
    /// </summary>
    /// <returns>The new note's id, so the caller need not guess it.</returns>
    /// <exception cref="DomainException">The author or the body is blank, or the body is too long.</exception>
    public Guid AddNote(string authorId, string authorName, string body)
    {
        var note = new OrderNote(Id, authorId, authorName, body);
        _notes.Add(note);
        return note.Id;
    }

    /// <summary>
    /// Appends one history row, in the same unit of work as the status change that called it. Every
    /// transition method calls this and nothing else does; see <see cref="OrderStatusHistory"/> for why
    /// it is not fed from the domain events instead.
    /// </summary>
    private void RecordTransition(OrderStatus? from, OrderStatus to, string? reason = null)
        => _statusHistory.Add(new OrderStatusHistory(Id, from, to, reason));

    /// <summary>
    /// The largest total the <c>numeric(18,2)</c> column holds (Ordering audit L1). Above it Postgres
    /// refused the write with 22003, which nothing maps, so the request was a 500.
    /// </summary>
    public const decimal MaxTotal = 9_999_999_999_999_999.99m;

    /// <summary>Checked before the order changes, so a refused line leaves the order as it was.</summary>
    private static void EnsureStorable(decimal total)
    {
        if (total > MaxTotal)
            throw new DomainException(
                $"Order total must not exceed {MaxTotal.ToString("0.00", CultureInfo.InvariantCulture)}.");
    }

    private void RecalculateTotal()
    {
        TotalPrice = _items.Sum(i => i.SubTotal);
    }

    /// <summary>
    /// Tells Payment the order now costs something else (frontend-contracts F-47). Payment charges the amount it
    /// recorded when the order was created, and <see cref="MarkAsPaid"/> refuses any payment that differs from
    /// <see cref="TotalPrice"/>. So an item change it never heard about left a paid order Pending for good.
    /// <para>Only when the total actually moved: setting a line to the quantity it already has, or swapping lines of
    /// equal value, changes nothing Payment needs to know.</para>
    /// </summary>
    private void RaiseTotalChangedIfMoved(decimal previousTotal)
    {
        if (TotalPrice == previousTotal)
        {
            return;
        }

        AddDomainEvent(new OrderTotalChangedDomainEvent
        {
            OrderId = Id,
            UserId = UserId,
            NewTotal = TotalPrice
        });
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
