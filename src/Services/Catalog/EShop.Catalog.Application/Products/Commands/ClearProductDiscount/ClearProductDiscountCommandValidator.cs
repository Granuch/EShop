using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.ClearProductDiscount;

public class ClearProductDiscountCommandValidator : AbstractValidator<ClearProductDiscountCommand>
{
    public ClearProductDiscountCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");
    }
}
