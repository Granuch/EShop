using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.UpdateProductAttribute;

/// <summary>
/// Validator for UpdateProductAttributeCommand. Mirrors AddProductAttributeCommandValidator — the
/// slices are kept independent rather than sharing a validator across command folders.
/// </summary>
public class UpdateProductAttributeCommandValidator : AbstractValidator<UpdateProductAttributeCommand>
{
    public UpdateProductAttributeCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.AttributeId)
            .NotEmpty().WithMessage("Attribute ID is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Attribute name is required")
            .MaximumLength(100).WithMessage("Attribute name must not exceed 100 characters");

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Attribute value is required")
            .MaximumLength(200).WithMessage("Attribute value must not exceed 200 characters");
    }
}
