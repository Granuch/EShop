using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Validator for CreateProductCommand
/// </summary>
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    private const int MaxImages = 10;
    private const int MaxAttributes = 50;

    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required")
            .MaximumLength(200).WithMessage("Product name must not exceed 200 characters");

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU is required")
            .MaximumLength(50).WithMessage("SKU must not exceed 50 characters")
            .Matches(@"^[A-Za-z0-9\-_]+$").WithMessage("SKU must contain only alphanumeric characters, hyphens, and underscores");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than 0");

        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("Stock quantity cannot be negative");

        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Category is required");

        RuleFor(x => x.Images)
            .NotNull().WithMessage("Images cannot be null")
            .Must(images => images is null || images.Count <= MaxImages)
                .WithMessage($"A product cannot have more than {MaxImages} images")
            .Must(HaveUniqueUrls)
                .WithMessage("Image URLs must be unique within a product");

        RuleForEach(x => x.Images)
            .SetValidator(new CreateProductImageRequestValidator());

        RuleFor(x => x.Attributes)
            .NotNull().WithMessage("Attributes cannot be null")
            .Must(attributes => attributes is null || attributes.Count <= MaxAttributes)
                .WithMessage($"A product cannot have more than {MaxAttributes} attributes")
            .Must(HaveUniqueNames)
                .WithMessage("Attribute names must be unique within a product");

        RuleForEach(x => x.Attributes)
            .SetValidator(new CreateProductAttributeRequestValidator());
    }

    // Duplicate detection matches the domain's comparison: trimmed and case-insensitive.
    // Entries that are empty are left to the per-item validators to report.
    private static bool HaveUniqueUrls(IReadOnlyList<CreateProductImageRequest>? images)
    {
        if (images is null)
            return true;

        var urls = images
            .Where(i => !string.IsNullOrWhiteSpace(i.Url))
            .Select(i => i.Url.Trim())
            .ToList();

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).Count() == urls.Count;
    }

    private static bool HaveUniqueNames(IReadOnlyList<CreateProductAttributeRequest>? attributes)
    {
        if (attributes is null)
            return true;

        var names = attributes
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => a.Name.Trim())
            .ToList();

        return names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count;
    }
}
