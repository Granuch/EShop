using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.Application.Orders.Queries;

/// <summary>
/// DTO for order in list/detail view
/// </summary>
public record OrderDto
{
    public Guid Id { get; init; }
    public string UserId { get; init; } = string.Empty;
    public decimal TotalPrice { get; init; }
    public OrderStatus Status { get; init; }
    public string? PaymentIntentId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTime? ShippedAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime? CancelledAt { get; init; }
    public string? CancellationReason { get; init; }
    public AddressDto ShippingAddress { get; init; } = null!;
    public List<OrderItemDto> Items { get; init; } = new();
}

public record OrderItemDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public decimal SubTotal { get; init; }
}

public record AddressDto
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
}
