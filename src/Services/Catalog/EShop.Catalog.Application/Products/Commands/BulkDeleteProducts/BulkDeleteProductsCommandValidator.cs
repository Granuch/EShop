using EShop.Catalog.Application.Products.Bulk;
using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.BulkDeleteProducts;

public class BulkDeleteProductsCommandValidator : AbstractValidator<BulkDeleteProductsCommand>
{
    public BulkDeleteProductsCommandValidator()
    {
        RuleFor(x => x.ProductIds).BulkProductIds();
    }
}
