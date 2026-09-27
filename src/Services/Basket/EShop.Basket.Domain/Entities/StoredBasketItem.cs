namespace EShop.Basket.Domain.Entities;

/// <summary>
/// One line of a stored basket, as the repository read it. <paramref name="AddedAt"/> is null for a document written
/// before Basket audit S9 (L1) began storing it; the line then takes the basket's creation time. <paramref
/// name="MainImageUrl"/> is null for a document written before this field existed, same as for any product Catalog
/// reported with no image.
/// </summary>
public sealed record StoredBasketItem(
    Guid ProductId,
    string ProductName,
    decimal Price,
    int Quantity,
    DateTime? AddedAt = null,
    string? MainImageUrl = null);
