using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>
/// Validator for CreateProductCommand
/// </summary>
public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        // TODO: Validate product name
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required")
            .MaximumLength(200);

        // TODO: Validate SKU uniqueness and format
        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU is required")
            .MaximumLength(50);

        // TODO: Validate price is positive
        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than 0");

        // TODO: Validate stock quantity
        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("Stock quantity cannot be negative");

        // TODO: Validate category exists
        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Category is required");
    }
}
