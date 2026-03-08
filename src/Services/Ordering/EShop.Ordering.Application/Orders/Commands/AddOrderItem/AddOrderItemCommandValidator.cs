using FluentValidation;

namespace EShop.Ordering.Application.Orders.Commands.AddOrderItem;

public class AddOrderItemCommandValidator : AbstractValidator<AddOrderItemCommand>
{
    public AddOrderItemCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required");

        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        RuleFor(x => x.ProductName)
            .NotEmpty().WithMessage("Product name is required");

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Unit price cannot be negative");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0");
    }
}
