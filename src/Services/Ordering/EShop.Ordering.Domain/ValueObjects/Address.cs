using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;
using System.Text.RegularExpressions;

namespace EShop.Ordering.Domain.ValueObjects;

/// <summary>
/// Address value object.
///
/// <para>
/// <see cref="Validate"/> is the single statement of what a storable address is. The constructor
/// enforces it, and both create validators report it field by field, so a request the validator
/// accepts is one this constructor accepts (audit M1). Basket's checkout validator mirrors these
/// patterns too — an address Basket accepts and Ordering refuses is a lost order.
/// </para>
/// </summary>
public class Address : ValueObject
{
    private static readonly Regex StreetRegex = new(@"^[\p{L}\p{N}\s\.,\-/#]{3,150}$", RegexOptions.Compiled);
    private static readonly Regex CityStateRegex = new(@"^[\p{L}\s\.'\-]{2,100}$", RegexOptions.Compiled);
    private static readonly Regex CountryRegex = new(@"^[A-Za-z]{2}$", RegexOptions.Compiled);
    private static readonly Regex UsZipRegex = new(@"^\d{5}(?:-\d{4})?$", RegexOptions.Compiled);

    public string Street { get; private init; } = string.Empty;
    public string City { get; private init; } = string.Empty;
    public string State { get; private init; } = string.Empty;
    public string ZipCode { get; private init; } = string.Empty;
    public string Country { get; private init; } = string.Empty;

    // Parameterless constructor for EF Core
    private Address() { }

    /// <exception cref="DomainException">
    /// The address breaks a rule in <see cref="Validate"/>. A DomainException, not an
    /// ArgumentException: the shared middleware maps the former to 400 and the latter to nothing, so
    /// a bad country code used to come back as a 500.
    /// </exception>
    public Address(string street, string city, string state, string zipCode, string country)
    {
        var problems = Validate(street, city, state, zipCode, country);
        if (problems.Count > 0)
        {
            throw new DomainException(string.Join(" ", problems.Select(p => p.Message)));
        }

        Street = Normalize(street);
        City = Normalize(city);
        State = Normalize(state);
        Country = NormalizeCountry(country);
        ZipCode = Normalize(zipCode);
    }

    /// <summary>
    /// Every rule the address breaks, each against the name of the field it concerns (which is also
    /// the property name on the create commands). Empty when the address is storable.
    /// </summary>
    public static IReadOnlyList<AddressProblem> Validate(
        string? street, string? city, string? state, string? zipCode, string? country)
    {
        var problems = new List<AddressProblem>();

        if (!StreetRegex.IsMatch(Normalize(street)))
        {
            problems.Add(new(nameof(Street), "Street must be 3-150 characters and contain only valid address symbols."));
        }

        if (!CityStateRegex.IsMatch(Normalize(city)))
        {
            problems.Add(new(nameof(City), "City must be 2-100 characters and contain only letters and common separators."));
        }

        if (!CityStateRegex.IsMatch(Normalize(state)))
        {
            problems.Add(new(nameof(State), "State must be 2-100 characters and contain only letters and common separators."));
        }

        var normalizedCountry = NormalizeCountry(country);
        if (!CountryRegex.IsMatch(normalizedCountry))
        {
            problems.Add(new(nameof(Country), "Country must be a 2-letter ISO code."));
        }

        var normalizedZipCode = Normalize(zipCode);
        if (normalizedZipCode.Length is < 3 or > 12)
        {
            problems.Add(new(nameof(ZipCode), "Zip code must be between 3 and 12 characters."));
        }
        else if (normalizedCountry == "US" && !UsZipRegex.IsMatch(normalizedZipCode))
        {
            problems.Add(new(nameof(ZipCode), "Zip code must match US postal code format (12345 or 12345-6789)."));
        }

        return problems;
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

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();

    private static string NormalizeCountry(string? country) => Normalize(country).ToUpperInvariant();
}

/// <summary>One broken address rule: the field it concerns and a message fit to show the client.</summary>
public sealed record AddressProblem(string Field, string Message);
