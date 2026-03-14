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
    /// Legacy string address for backward compatibility.
    /// New publishers should populate <see cref="ShippingAddressDetails"/> instead.
    /// </summary>
    public string ShippingAddress { get; init; } = string.Empty;

    /// <summary>
    /// Structured shipping address. Preferred over <see cref="ShippingAddress"/>.
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
