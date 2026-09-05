using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.RemoveProductImage;

public class RemoveProductImageCommandValidator : AbstractValidator<RemoveProductImageCommand>
{
    public RemoveProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.ImageId)
            .NotEmpty().WithMessage("Image ID is required");
    }
}
