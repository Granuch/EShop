using EShop.BuildingBlocks.Domain;
using System.Text.RegularExpressions;

namespace EShop.Ordering.Domain.ValueObjects;

/// <summary>
/// Address value object
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

    public Address(string street, string city, string state, string zipCode, string country)
    {
        Street = ValidateStreet(street);
        City = ValidateCityOrState(city, nameof(city));
        State = ValidateCityOrState(state, nameof(state));
        Country = ValidateCountry(country);
        ZipCode = ValidateZipCode(zipCode, Country);
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

    private static string ValidateStreet(string street)
    {
        var normalizedStreet = (street ?? string.Empty).Trim();
        if (!StreetRegex.IsMatch(normalizedStreet))
        {
            throw new ArgumentException("Street must be 3-150 characters and contain only valid address symbols.");
        }

        return normalizedStreet;
    }

    private static string ValidateCityOrState(string value, string fieldName)
    {
        var normalizedValue = (value ?? string.Empty).Trim();
        if (!CityStateRegex.IsMatch(normalizedValue))
        {
            throw new ArgumentException($"{fieldName} must be 2-100 characters and contain only letters and common separators.");
        }

        return normalizedValue;
    }

    private static string ValidateCountry(string country)
    {
        var normalizedCountry = (country ?? string.Empty).Trim().ToUpperInvariant();
        if (!CountryRegex.IsMatch(normalizedCountry))
        {
            throw new ArgumentException("Country must be a 2-letter ISO code.");
        }

        return normalizedCountry;
    }

    private static string ValidateZipCode(string zipCode, string country)
    {
        var normalizedZipCode = (zipCode ?? string.Empty).Trim();
        if (normalizedZipCode.Length is < 3 or > 12)
        {
            throw new ArgumentException("Zip code must be between 3 and 12 characters.");
        }

        if (country == "US" && !UsZipRegex.IsMatch(normalizedZipCode))
        {
            throw new ArgumentException("Zip code must match US postal code format (12345 or 12345-6789).");
        }

        return normalizedZipCode;
    }
}
