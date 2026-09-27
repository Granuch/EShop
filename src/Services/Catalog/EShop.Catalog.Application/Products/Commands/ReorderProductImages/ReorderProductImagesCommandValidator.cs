using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.ReorderProductImages;

/// <summary>
/// Validator for ReorderProductImagesCommand.
/// </summary>
/// <remarks>
/// It checks only what the request can be judged on by itself: that a list arrived, that it is not
/// empty, and that it holds no obviously invalid ids. "Exactly this product's images, each once"
/// needs the persisted product, so it lives in <c>Product.ReorderImages</c> — the same split as the
/// discount rules, where <c>&gt; 0</c> is a validator rule and <c>&lt; Price</c> is a domain one.
/// </remarks>
public class ReorderProductImagesCommandValidator : AbstractValidator<ReorderProductImagesCommand>
{
    public ReorderProductImagesCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        // Cascade(Stop) rather than a trailing .When(...): a trailing When guards every rule above
        // it in the chain, which makes adding a rule later a silent no-op.
        RuleFor(x => x.ImageIds)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("The ordered list of image IDs is required")
            .Must(ids => ids!.Count > 0).WithMessage("At least one image ID is required");

        RuleForEach(x => x.ImageIds)
            .NotEmpty().WithMessage("Image IDs must not be empty");
    }
}
