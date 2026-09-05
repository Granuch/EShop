using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Validator for a single inline product attribute.
/// Mirrors the rules ProductAttribute enforces in the domain so malformed input is rejected
/// as a 400 at the API boundary rather than surfacing as a DomainException from the aggregate.
/// </summary>
public class CreateProductAttributeRequestValidator : AbstractValidator<CreateProductAttributeRequest>
{
    public CreateProductAttributeRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Attribute name is required")
            .MaximumLength(100).WithMessage("Attribute name must not exceed 100 characters");

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Attribute value is required")
            .MaximumLength(200).WithMessage("Attribute value must not exceed 200 characters");
    }
}
