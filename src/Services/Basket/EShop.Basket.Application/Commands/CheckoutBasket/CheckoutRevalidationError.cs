using EShop.BuildingBlocks.Application;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

/// <summary>
/// Basket audit S6 (H5, D6). Checkout re-read every line from Catalog and at least one no longer matches, so nothing
/// was ordered. It carries each affected line, which the endpoint returns as the problem's <c>lines</c> member (409).
/// </summary>
public sealed record CheckoutRevalidationError(IReadOnlyList<CheckoutLineProblem> Lines)
    : Error(ErrorCode, "Some items in the basket changed in the catalog. Review them and check out again.")
{
    public const string ErrorCode = "Basket.CheckoutRevalidationFailed";
}

/// <summary>One basket line that failed revalidation, and why.</summary>
/// <param name="Reason"><see cref="Unavailable"/>, <see cref="OutOfStock"/> or <see cref="Repriced"/>.</param>
/// <param name="AvailableQuantity">What Catalog has in stock; null when the product is unavailable.</param>
/// <param name="CatalogPrice">Catalog's current price, which the basket now holds; null when unavailable.</param>
public sealed record CheckoutLineProblem(
    Guid ProductId,
    string Reason,
    int RequestedQuantity,
    decimal BasketPrice,
    int? AvailableQuantity,
    decimal? CatalogPrice)
{
    /// <summary>Missing from the public catalog: deleted, unpublished or discontinued.</summary>
    public const string Unavailable = "Unavailable";

    /// <summary>Catalog has fewer in stock than the line asks for.</summary>
    public const string OutOfStock = "OutOfStock";

    /// <summary>The price changed; the basket has taken the new one.</summary>
    public const string Repriced = "Repriced";
}
