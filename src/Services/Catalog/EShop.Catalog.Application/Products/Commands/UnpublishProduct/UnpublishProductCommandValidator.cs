using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.UnpublishProduct;

public class UnpublishProductCommandValidator : AbstractValidator<UnpublishProductCommand>
{
    public UnpublishProductCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");
    }
}
