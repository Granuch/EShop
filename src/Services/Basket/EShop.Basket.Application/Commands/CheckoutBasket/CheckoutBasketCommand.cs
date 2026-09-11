using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

/// <summary>
/// Command to checkout basket and create order
/// </summary>
public record CheckoutBasketCommand : IRequest<Result<Guid>>, ICacheInvalidatingCommand
{
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Nullable so an explicit JSON <c>null</c> reaches the validator (a 400 naming the field)
    /// instead of failing model binding.
    /// </summary>
    public CheckoutAddress? ShippingAddress { get; init; }

    public string PaymentMethod { get; init; } = string.Empty;

    public IEnumerable<string> CacheKeysToInvalidate =>
    [
        $"basket:user:{UserId}"
    ];
}

/// <summary>
/// The structured address checkout requires. It replaced a single free-text string, which Ordering
/// could not reliably turn into an address (Ordering audit C2).
/// </summary>
public record CheckoutAddress
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2, e.g. <c>US</c>, <c>UA</c>.</summary>
    public string Country { get; init; } = string.Empty;
}
