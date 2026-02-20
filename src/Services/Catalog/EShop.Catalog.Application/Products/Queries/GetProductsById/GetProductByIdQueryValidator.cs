using FluentValidation;

namespace EShop.Catalog.Application.Products.Queries.GetProductsById;

public class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");
    }
}
