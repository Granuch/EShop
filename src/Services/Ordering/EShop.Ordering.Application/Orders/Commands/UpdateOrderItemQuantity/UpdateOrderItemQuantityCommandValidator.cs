using FluentValidation;

namespace EShop.Ordering.Application.Orders.Commands.UpdateOrderItemQuantity;

/// <summary>
/// Deliberately the same rules as <c>AddOrderItemCommandValidator</c>, with no upper bound on the
/// quantity: neither create nor add has one, so capping only the edit would make a quantity reachable
/// by adding a line and unreachable by correcting one. What actually bounds it is
/// <c>Order.MaxTotal</c>, checked in the aggregate before the line changes.
/// </summary>
public class UpdateOrderItemQuantityCommandValidator : AbstractValidator<UpdateOrderItemQuantityCommand>
{
    public UpdateOrderItemQuantityCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required");

        RuleFor(x => x.ItemId)
            .NotEmpty().WithMessage("Item ID is required");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than 0");
    }
}
