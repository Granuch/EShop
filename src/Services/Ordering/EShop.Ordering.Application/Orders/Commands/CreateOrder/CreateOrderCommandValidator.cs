using FluentValidation;

namespace EShop.Ordering.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Validator for CreateOrderCommand
/// </summary>
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Order must have at least one item");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty().WithMessage("Product ID is required");

            item.RuleFor(i => i.ProductName)
                .NotEmpty().WithMessage("Product name is required");

            item.RuleFor(i => i.Price)
                .GreaterThanOrEqualTo(0).WithMessage("Price cannot be negative");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than 0");
        });

        RuleFor(x => x.Street)
            .NotEmpty().WithMessage("Street is required");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required");

        RuleFor(x => x.Country)
            .NotEmpty().WithMessage("Country is required");
    }
}
