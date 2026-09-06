using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.SetMainProductImage;

public class SetMainProductImageCommandValidator : AbstractValidator<SetMainProductImageCommand>
{
    public SetMainProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.ImageId)
            .NotEmpty().WithMessage("Image ID is required");
    }
}
