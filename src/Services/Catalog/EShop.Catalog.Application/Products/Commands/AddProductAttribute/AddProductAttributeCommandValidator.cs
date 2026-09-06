using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.AddProductAttribute;

/// <summary>
/// Validator for AddProductAttributeCommand.
/// Repeats the per-attribute rules used when creating a product with inline attributes —
/// the two slices are kept independent rather than sharing a validator across command folders.
/// </summary>
public class AddProductAttributeCommandValidator : AbstractValidator<AddProductAttributeCommand>
{
    public AddProductAttributeCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Attribute name is required")
            .MaximumLength(100).WithMessage("Attribute name must not exceed 100 characters");

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Attribute value is required")
            .MaximumLength(200).WithMessage("Attribute value must not exceed 200 characters");
    }
}
