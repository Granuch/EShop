using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

/// <summary>
/// Command to checkout basket and create order
/// </summary>
public record CheckoutBasketCommand : IRequest<Result<Guid>>
{
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Nullable so an explicit JSON <c>null</c> reaches the validator (a 400 naming the field)
    /// instead of failing model binding.
    ///
    /// <para><b>Personal data, so <c>[SensitiveData]</c> (Basket audit S10, M5).</b> <c>LoggingBehavior</c> logs the whole
    /// command at Information level, and none of street, city or zip code matches its redaction list, so every checkout
    /// wrote a home address to Seq and to log files kept for 30 days.</para>
    /// </summary>
    [SensitiveData]
    public CheckoutAddress? ShippingAddress { get; init; }

    public string PaymentMethod { get; init; } = string.Empty;
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
