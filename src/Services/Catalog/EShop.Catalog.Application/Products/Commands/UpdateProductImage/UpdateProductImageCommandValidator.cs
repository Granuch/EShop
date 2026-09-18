using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.UpdateProductImage;

/// <summary>
/// Validator for UpdateProductImageCommand. Mirrors AddProductImageCommandValidator — the two
/// slices are kept independent rather than sharing a validator across command folders, matching
/// the convention the inline-create and sub-resource validators already follow.
/// </summary>
public class UpdateProductImageCommandValidator : AbstractValidator<UpdateProductImageCommand>
{
    public UpdateProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.ImageId)
            .NotEmpty().WithMessage("Image ID is required");

        // Shape (absolute http/https) is the domain's job, so it is checked once in
        // ProductImage rather than duplicated here as a second, drifting rule.
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("Image URL is required")
            .MaximumLength(500).WithMessage("Image URL must not exceed 500 characters");

        // No NotEmpty: an absent alt text is the legitimate way to clear it.
        RuleFor(x => x.AltText)
            .MaximumLength(200).WithMessage("Alt text must not exceed 200 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.AltText));
    }
}
