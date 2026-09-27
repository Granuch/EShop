using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.ReplaceProductAttributes;

/// <summary>
/// Validator for a single attribute in a whole-set replacement. Mirrors the rules
/// <c>ProductAttribute</c> enforces, so malformed input is a 400 at the API boundary rather than a
/// DomainException from the aggregate.
/// </summary>
public class ReplaceProductAttributeRequestValidator : AbstractValidator<ReplaceProductAttributeRequest>
{
    public ReplaceProductAttributeRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Attribute name is required")
            .MaximumLength(100).WithMessage("Attribute name must not exceed 100 characters");

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Attribute value is required")
            .MaximumLength(200).WithMessage("Attribute value must not exceed 200 characters");
    }
}

/// <summary>
/// Validator for ReplaceProductAttributesCommand.
/// </summary>
public class ReplaceProductAttributesCommandValidator : AbstractValidator<ReplaceProductAttributesCommand>
{
    private const int MaxAttributes = 50;

    public ReplaceProductAttributesCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        // NotNull, not NotEmpty: an explicit empty list means "this product has no attributes" and
        // is the only way to clear the whole set. Absent, though, is a malformed request — there is
        // nothing to replace the set WITH — and System.Text.Json presents an omitted array as null.
        RuleFor(x => x.Attributes)
            .NotNull().WithMessage("The attributes list is required (send an empty array to clear all attributes)");

        RuleFor(x => x.Attributes)
            .Must(attributes => attributes!.Count <= MaxAttributes)
                .WithMessage($"A product cannot have more than {MaxAttributes} attributes")
            .Must(HaveUniqueNames)
                .WithMessage("Attribute names must be unique within a product")
            .When(x => x.Attributes is not null);

        RuleForEach(x => x.Attributes)
            .SetValidator(new ReplaceProductAttributeRequestValidator());
    }

    // Duplicate detection matches the domain's comparison: trimmed and case-insensitive.
    // Entries that are blank are left to the per-item validator to report.
    private static bool HaveUniqueNames(IReadOnlyList<ReplaceProductAttributeRequest>? attributes)
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
