using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Domain.ValueObjects;

/// <summary>
/// Address value object
/// </summary>
public class Address : ValueObject
{
    public string Street { get; private init; } = string.Empty;
    public string City { get; private init; } = string.Empty;
    public string State { get; private init; } = string.Empty;
    public string ZipCode { get; private init; } = string.Empty;
    public string Country { get; private init; } = string.Empty;

    // Parameterless constructor for EF Core
    private Address() { }

    public Address(string street, string city, string state, string zipCode, string country)
    {
        Street = !string.IsNullOrWhiteSpace(street)
            ? street
            : throw new ArgumentException("Street is required");
        City = city;
        State = state;
        ZipCode = zipCode;
        Country = country;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return ZipCode;
        yield return Country;
    }

    public override string ToString() => $"{Street}, {City}, {State} {ZipCode}, {Country}";

    // TODO: Add address validation (e.g., zip code format)
}
