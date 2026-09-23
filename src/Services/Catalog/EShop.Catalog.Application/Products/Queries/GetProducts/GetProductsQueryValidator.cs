using FluentValidation;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

public class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    public GetProductsQueryValidator()
    {
        // The rules run against the Effective* accessors so an omitted parameter validates as its
        // default, but the error is reported under the name the client actually sends. Without the
        // override a bad pageSize came back as an error on "EffectivePageSize" — a property no
        // client has ever heard of.
        RuleFor(x => x.EffectivePageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1")
            .OverridePropertyName(nameof(GetProductsQuery.PageNumber));

        RuleFor(x => x.EffectivePageSize)
            .GreaterThanOrEqualTo(1).WithMessage("Page size must be at least 1")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100")
            .OverridePropertyName(nameof(GetProductsQuery.PageSize));

        // H4. Rejected, not ignored — see GetProductsQuery.Cursor.
        RuleFor(x => x.Cursor)
            .Empty()
            .WithMessage("Cursor paging is served by GET /api/v1/products/newest. This endpoint pages by PageNumber.");

        // The filter rules are shared with the export's validator (admin panel S16).
        ProductFilterRules.Apply(this);
    }
}
