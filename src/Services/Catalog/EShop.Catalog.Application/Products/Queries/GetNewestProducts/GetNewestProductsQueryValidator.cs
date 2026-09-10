using FluentValidation;

namespace EShop.Catalog.Application.Products.Queries.GetNewestProducts;

public class GetNewestProductsQueryValidator : AbstractValidator<GetNewestProductsQuery>
{
    public GetNewestProductsQueryValidator()
    {
        // Written against the bound property, not an Effective* accessor, so the error names a
        // parameter the client actually sends.
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).When(x => x.PageSize.HasValue)
            .WithMessage("Page size must be between 1 and 100");

        // A cursor we cannot read is a 400, never "start from the beginning" — silently restarting
        // is the loop a client paging by cursor could never detect.
        RuleFor(x => x.Cursor)
            .Must(cursor => ProductCursor.TryDecode(cursor, out _))
            .When(x => !string.IsNullOrEmpty(x.Cursor))
            .WithMessage("Cursor is not valid. Pass the nextCursor from a previous response unchanged, or omit it for the first page.");

        RuleFor(x => x.MinPrice)
            .GreaterThanOrEqualTo(0).When(x => x.MinPrice.HasValue)
            .WithMessage("Minimum price cannot be negative");

        RuleFor(x => x.MaxPrice)
            .GreaterThan(x => x.MinPrice ?? 0).When(x => x.MaxPrice.HasValue && x.MinPrice.HasValue)
            .WithMessage("Maximum price must be greater than minimum price");

        RuleFor(x => x.SearchTerm)
            .MinimumLength(2).When(x => !string.IsNullOrEmpty(x.SearchTerm))
            .WithMessage("Search term must be at least 2 characters")
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.SearchTerm))
            .WithMessage("Search term must not exceed 200 characters");
    }
}
