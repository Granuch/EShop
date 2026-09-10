using FluentValidation;

namespace EShop.Catalog.Application.Products.Queries.GetProductByCategory;

public class GetProductByCategoryQueryValidator : AbstractValidator<GetProductByCategoryQuery>
{
    public GetProductByCategoryQueryValidator()
    {
        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Category ID is required");

        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).When(x => x.PageNumber.HasValue)
            .WithMessage("Page number must be at least 1");

        // Same ceiling as GET /products. Without it, retiring the 200 cap would have replaced a
        // silent truncation with an unbounded read.
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).When(x => x.PageSize.HasValue)
            .WithMessage("Page size must be between 1 and 100");
    }
}
