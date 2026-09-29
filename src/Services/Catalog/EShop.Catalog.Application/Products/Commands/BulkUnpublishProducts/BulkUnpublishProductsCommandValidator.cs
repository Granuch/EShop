using EShop.Catalog.Application.Products.Bulk;
using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.BulkUnpublishProducts;

public class BulkUnpublishProductsCommandValidator : AbstractValidator<BulkUnpublishProductsCommand>
{
    public BulkUnpublishProductsCommandValidator()
    {
        RuleFor(x => x.ProductIds).BulkProductIds();
    }
}
