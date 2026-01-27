namespace EShop.Basket.Application.Queries.GetBasket;

/// <summary>
/// DTO for basket
/// </summary>
public record BasketDto
{
    public string UserId { get; init; } = string.Empty;
    public List<BasketItemDto> Items { get; init; } = new();
    public decimal TotalPrice { get; init; }
    public int TotalItems { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastModifiedAt { get; init; }
}

public record BasketItemDto
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public decimal SubTotal { get; init; }
}
