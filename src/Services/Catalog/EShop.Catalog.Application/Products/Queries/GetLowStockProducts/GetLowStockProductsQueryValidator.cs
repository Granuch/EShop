using FluentValidation;

namespace EShop.Catalog.Application.Products.Queries.GetLowStockProducts;

/// <summary>
/// Validator for GetLowStockProductsQuery.
/// </summary>
public class GetLowStockProductsQueryValidator : AbstractValidator<GetLowStockProductsQuery>
{
    public GetLowStockProductsQueryValidator()
    {
        // Strictly-below semantics make threshold 0 an always-empty page, which is a caller error
        // rather than a legitimate request; 1 is the way to ask for "out of stock".
        RuleFor(x => x.Threshold)
            .GreaterThan(0).WithMessage("Threshold must be greater than 0 (use threshold=1 for out-of-stock products)")
            .When(x => x.Threshold.HasValue);

        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0")
            .When(x => x.PageNumber.HasValue);

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100")
            .When(x => x.PageSize.HasValue);
    }
}
