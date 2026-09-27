using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.UpdateProduct;

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than 0");

        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("Stock quantity cannot be negative");

        // Admin panel S2. Each rule is guarded by its own When(...), because omitting the field
        // means "leave it alone" and must stay valid. Note the guard applies to the whole preceding
        // chain for that property, so a rule added above one of these inherits it — that is the
        // point here, but it is also how a NotEmpty() added to such a chain becomes a silent no-op
        // (the UpdateProfileCommandValidator lesson).
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name cannot be blank")
            .MaximumLength(200).WithMessage("Product name must be 200 characters or fewer")
            .When(x => x.Name is not null);

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU cannot be blank")
            .MaximumLength(50).WithMessage("SKU must be 50 characters or fewer")
            .When(x => x.Sku is not null);

        // Not NotEmpty: a blank description is the documented way to clear one.
        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters")
            .When(x => x.Description is not null);

        RuleFor(x => x.CategoryId)
            .NotEqual(Guid.Empty).WithMessage("Category ID must not be empty")
            .When(x => x.CategoryId is not null);
    }
}
