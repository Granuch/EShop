namespace EShop.Basket.Domain.Entities;

/// <summary>
/// One line of a stored basket, as the repository read it. <paramref name="AddedAt"/> is null for a document written
/// before Basket audit S9 (L1) began storing it; the line then takes the basket's creation time.
/// </summary>
public sealed record StoredBasketItem(
    Guid ProductId,
    string ProductName,
    decimal Price,
    int Quantity,
    DateTime? AddedAt = null);
