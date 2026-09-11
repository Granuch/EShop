using System.Text.RegularExpressions;
using FluentValidation;

namespace EShop.Basket.Application.Commands.CheckoutBasket;

public class CheckoutBasketCommandValidator : AbstractValidator<CheckoutBasketCommand>
{
    // These mirror Ordering's Address value object (src/Services/Ordering/EShop.Ordering.Domain/
    // ValueObjects/Address.cs) rule for rule, including trimming before matching. They have to: Basket
    // clears the basket at checkout, before Ordering ever reads the event, so an address Basket accepts
    // and Ordering rejects is a lost order. Change one, change the other.
    private static readonly Regex StreetPattern = new(@"^[\p{L}\p{N}\s\.,\-/#]{3,150}$", RegexOptions.Compiled);
    private static readonly Regex CityOrStatePattern = new(@"^[\p{L}\s\.'\-]{2,100}$", RegexOptions.Compiled);
    private static readonly Regex CountryPattern = new(@"^[A-Za-z]{2}$", RegexOptions.Compiled);
    private static readonly Regex UsZipPattern = new(@"^\d{5}(?:-\d{4})?$", RegexOptions.Compiled);

    public CheckoutBasketCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.ShippingAddress)
            .NotNull().WithMessage("Shipping address is required");

        RuleFor(x => x.ShippingAddress).ChildRules(address =>
        {
            address.RuleFor(a => a.Street)
                .Must(v => Matches(StreetPattern, v))
                .WithMessage("Street must be 3-150 characters of letters, digits, spaces and . , - / #");

            address.RuleFor(a => a.City)
                .Must(v => Matches(CityOrStatePattern, v))
                .WithMessage("City must be 2-100 characters of letters, spaces and . ' -");

            address.RuleFor(a => a.State)
                .Must(v => Matches(CityOrStatePattern, v))
                .WithMessage("State must be 2-100 characters of letters, spaces and . ' -");

            address.RuleFor(a => a.Country)
                .Must(v => Matches(CountryPattern, v))
                .WithMessage("Country must be a 2-letter ISO code, e.g. US");

            address.RuleFor(a => a.ZipCode)
                .Must(v => v?.Trim().Length is >= 3 and <= 12)
                .WithMessage("Zip code must be 3-12 characters");

            address.RuleFor(a => a.ZipCode)
                .Must(v => Matches(UsZipPattern, v))
                .When(a => string.Equals(a.Country?.Trim(), "US", StringComparison.OrdinalIgnoreCase))
                .WithMessage("A US zip code must be 12345 or 12345-6789");
        });

        RuleFor(x => x.PaymentMethod)
            .NotEmpty().WithMessage("Payment method is required")
            .MaximumLength(100).WithMessage("Payment method must not exceed 100 characters");
    }

    private static bool Matches(Regex pattern, string? value) => value is not null && pattern.IsMatch(value.Trim());
}
