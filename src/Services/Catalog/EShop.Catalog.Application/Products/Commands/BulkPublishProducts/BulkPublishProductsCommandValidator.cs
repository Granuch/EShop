using EShop.Catalog.Application.Products.Bulk;
using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.BulkPublishProducts;

public class BulkPublishProductsCommandValidator : AbstractValidator<BulkPublishProductsCommand>
{
    public BulkPublishProductsCommandValidator()
    {
        RuleFor(x => x.ProductIds).BulkProductIds();
    }
}
