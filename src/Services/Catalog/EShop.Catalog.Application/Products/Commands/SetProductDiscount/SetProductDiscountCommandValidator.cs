using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.SetProductDiscount;

/// <summary>
/// Only the rules that can be checked from the request alone. "Below the list price" needs the
/// persisted product and therefore lives in <c>Product.SetDiscountPrice</c>.
/// </summary>
public class SetProductDiscountCommandValidator : AbstractValidator<SetProductDiscountCommand>
{
    public SetProductDiscountCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.DiscountPrice)
            .GreaterThan(0).WithMessage("Discount price must be greater than 0");
    }
}
