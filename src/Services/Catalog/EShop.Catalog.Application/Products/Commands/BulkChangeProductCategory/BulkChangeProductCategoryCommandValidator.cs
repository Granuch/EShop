using EShop.Catalog.Application.Products.Bulk;
using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.BulkChangeProductCategory;

public class BulkChangeProductCategoryCommandValidator : AbstractValidator<BulkChangeProductCategoryCommand>
{
    public BulkChangeProductCategoryCommandValidator()
    {
        RuleFor(x => x.ProductIds).BulkProductIds();

        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Category ID is required");
    }
}
