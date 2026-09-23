using EShop.Catalog.Application.Products.Bulk;
using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.BulkUpdateProductPrices;

public class BulkUpdateProductPricesCommandValidator : AbstractValidator<BulkUpdateProductPricesCommand>
{
    public BulkUpdateProductPricesCommandValidator()
    {
        // The id rules are the shared ones, applied to the ids the items carry.
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("At least one price is required")
            // A JSON null inside the array binds as a null item; refused here rather than dereferenced below.
            .Must(items => items is null || items.All(i => i is not null))
                .WithMessage("A price item must not be null");

        RuleFor(x => (IReadOnlyList<Guid>?)x.Items!.Select(i => i.ProductId).ToList())
            .BulkProductIds()
            .OverridePropertyName(nameof(BulkUpdateProductPricesCommand.Items))
            .When(x => x.Items is { Count: > 0 } && x.Items.All(i => i is not null));

        // The same bound as a single edit; the discount rule needs the stored product and is the domain's.
        RuleForEach(x => x.Items)
            .ChildRules(item => item.RuleFor(i => i.Price)
                .GreaterThan(0).WithMessage("Price must be greater than 0"))
            .When(x => x.Items is { Count: <= BulkProductLimits.MaxItemsPerRequest } && x.Items.All(i => i is not null));
    }
}
