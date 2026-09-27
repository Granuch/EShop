using FluentValidation;

namespace EShop.Catalog.Application.Products.Queries.GetDeletedProducts;

/// <summary>
/// Validator for GetDeletedProductsQuery. The page-size cap of 100 matches every other list read in
/// this service, and is what keeps the projection's per-row correlated subquery bounded.
/// </summary>
public class GetDeletedProductsQueryValidator : AbstractValidator<GetDeletedProductsQuery>
{
    public GetDeletedProductsQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0")
            .When(x => x.PageNumber.HasValue);

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100")
            .When(x => x.PageSize.HasValue);
    }
}
