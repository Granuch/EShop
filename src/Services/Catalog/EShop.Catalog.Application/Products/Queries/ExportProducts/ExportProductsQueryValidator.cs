using EShop.Catalog.Application.Products.Queries.GetProducts;
using FluentValidation;

namespace EShop.Catalog.Application.Products.Queries.ExportProducts;

public class ExportProductsQueryValidator : AbstractValidator<ExportProductsQuery>
{
    public ExportProductsQueryValidator()
    {
        // Exactly the list's filter rules, from the one place they are written.
        ProductFilterRules.Apply(this);
    }
}
