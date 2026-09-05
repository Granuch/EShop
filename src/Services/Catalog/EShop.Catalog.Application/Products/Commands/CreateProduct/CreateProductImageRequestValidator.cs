using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Validator for a single inline product image.
/// Mirrors the rules ProductImage enforces in the domain so malformed input is rejected as
/// a 400 at the API boundary rather than surfacing as a DomainException from the aggregate.
/// Deliberately has no file-extension check — extensionless CDN URLs are valid.
/// </summary>
public class CreateProductImageRequestValidator : AbstractValidator<CreateProductImageRequest>
{
    public CreateProductImageRequestValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("Image URL is required")
            .MaximumLength(500).WithMessage("Image URL must not exceed 500 characters")
            .Must(BeAnAbsoluteHttpUrl).WithMessage("Image URL must be an absolute HTTP/HTTPS URL");

        RuleFor(x => x.AltText)
            .MaximumLength(200).WithMessage("Image alt text must not exceed 200 characters");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Display order cannot be negative");
    }

    private static bool BeAnAbsoluteHttpUrl(string? url)
    {
        // An empty URL is already reported by NotEmpty; don't double-report it here.
        if (string.IsNullOrWhiteSpace(url))
            return true;

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsedUrl)
            && (parsedUrl.Scheme == Uri.UriSchemeHttp || parsedUrl.Scheme == Uri.UriSchemeHttps);
    }
}
