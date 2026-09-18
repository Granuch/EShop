using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.AdjustProductStock;

/// <summary>
/// Validator for AdjustProductStockCommand.
/// </summary>
public class AdjustProductStockCommandValidator : AbstractValidator<AdjustProductStockCommand>
{
    public AdjustProductStockCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        // The exactly-one rule is stated on the command as a whole rather than on either property,
        // because neither property alone can express it. Both messages matter: sending neither is a
        // caller that forgot the body, sending both is a caller that does not know which one wins —
        // and answering "delta wins" silently would be the worse outcome of the two.
        RuleFor(x => x)
            .Must(x => x.Delta.HasValue || x.Absolute.HasValue)
                .WithMessage("Supply either 'delta' (a relative movement) or 'absolute' (a stock-take quantity)")
            .Must(x => !(x.Delta.HasValue && x.Absolute.HasValue))
                .WithMessage("Supply only one of 'delta' and 'absolute', not both");

        RuleFor(x => x.Delta)
            .NotEqual(0).WithMessage("'delta' cannot be zero — omit the field instead of sending no movement")
            .When(x => x.Delta.HasValue);

        RuleFor(x => x.Absolute)
            .GreaterThanOrEqualTo(0).WithMessage("'absolute' cannot be negative")
            .When(x => x.Absolute.HasValue);

        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Reason must not exceed 500 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Reason));
    }
}
