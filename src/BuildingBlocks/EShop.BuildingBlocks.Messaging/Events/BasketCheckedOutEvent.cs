namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when basket is checked out
/// </summary>
public record BasketCheckedOutEvent : IntegrationEvent
{
    public string UserId { get; init; } = string.Empty;
    public List<CheckoutItem> Items { get; init; } = new();
    public decimal TotalPrice { get; init; }

    /// <summary>
    /// Display-only rendering of <see cref="ShippingAddressDetails"/>. Do not parse it: Ordering used
    /// to comma-split it and turned most real addresses into dead-lettered checkouts (Ordering audit C2).
    /// </summary>
    public string ShippingAddress { get; init; } = string.Empty;

    /// <summary>
    /// The shipping address. <b>Required</b> — Basket always sends it, and Ordering rejects a message
    /// without it. Nullable only so a message from an older publisher deserializes and can be rejected
    /// with a clear reason rather than failing to bind.
    /// </summary>
    public CheckoutShippingAddress? ShippingAddressDetails { get; init; }

    public string PaymentMethod { get; init; } = string.Empty;
}

public record CheckoutShippingAddress
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
}

public record CheckoutItem
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}
