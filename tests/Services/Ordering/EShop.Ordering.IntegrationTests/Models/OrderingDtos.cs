using EShop.Ordering.Domain.Entities;

namespace EShop.Ordering.IntegrationTests.Models;

/// <summary>
/// DTOs for Ordering API requests/responses in tests
/// </summary>

public record CreateOrderRequest
{
    public string UserId { get; init; } = string.Empty;
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public List<CreateOrderItemRequest> Items { get; init; } = new();
}

public record CreateOrderItemRequest
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
}

public record AddOrderItemRequest
{
    public Guid OrderId { get; init; }
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}

public record CancelOrderRequest
{
    public string Reason { get; init; } = string.Empty;
}

public record OrderResponse
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
    public AddressResponse ShippingAddress { get; init; } = null!;
    public List<OrderItemResponse> Items { get; init; } = new();
}

public record AddressResponse
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
}

public record OrderItemResponse
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
    public decimal SubTotal { get; init; }
}

public record CreatedResponse
{
    public Guid Id { get; init; }
}

public record ProblemDetailsResponse
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public int Status { get; init; }
    public string? TraceId { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
}

public record PagedOrderResponse
{
    public List<OrderResponse> Items { get; init; } = new();
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public bool HasPreviousPage { get; init; }
    public bool HasNextPage { get; init; }
}
