using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Basket.Domain.ValueObjects;

/// <summary>
/// Where a checked-out basket ships to. Structured because Ordering stores it as structured fields:
/// checkout used to carry one free-text string that Ordering split on commas, filling missing parts
/// with values its own <c>Address</c> rejects — so most real addresses never became an order
/// (Ordering audit C2). Format rules live in <c>CheckoutBasketCommandValidator</c>, which mirrors
/// Ordering's; this type only guarantees every part is present.
/// </summary>
public sealed record ShippingAddress
{
    private ShippingAddress(string street, string city, string state, string zipCode, string country)
    {
        Street = street;
        City = city;
        State = state;
        ZipCode = zipCode;
        Country = country;
    }

    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string ZipCode { get; }

    /// <summary>ISO 3166-1 alpha-2, upper-cased.</summary>
    public string Country { get; }

    public static ShippingAddress Create(string? street, string? city, string? state, string? zipCode, string? country)
        => new(
            Required(street, "street"),
            Required(city, "city"),
            Required(state, "state"),
            Required(zipCode, "zip code"),
            Required(country, "country").ToUpperInvariant());

    /// <summary>The same rendering Ordering's <c>Address.ToString()</c> produces.</summary>
    public override string ToString() => $"{Street}, {City}, {State} {ZipCode}, {Country}";

    private static string Required(string? value, string part)
        => string.IsNullOrWhiteSpace(value)
            ? throw new DomainException($"Shipping address {part} is required.")
            : value.Trim();
}
