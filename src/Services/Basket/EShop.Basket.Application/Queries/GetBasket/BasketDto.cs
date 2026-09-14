namespace EShop.Basket.Application.Queries.GetBasket;

/// <summary>
/// DTO for basket. A user with no basket gets an empty one — no items, zero totals, null dates — rather than a 404
/// (Basket audit S8, D8).
/// </summary>
public record BasketDto
{
    public string UserId { get; init; } = string.Empty;
    public List<BasketItemDto> Items { get; init; } = new();
    public decimal TotalPrice { get; init; }
    /// <summary>A long: a basket stored before the per-line limit (Basket audit S9, M2) can exceed <c>int.MaxValue</c>.</summary>
    public long TotalItems { get; init; }

    /// <summary>Null for a user who has no basket yet.</summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>Null for a user who has no basket yet.</summary>
    public DateTime? LastModifiedAt { get; init; }
}

public record BasketItemDto
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int Quantity { get; init; }
    public decimal SubTotal { get; init; }
}
