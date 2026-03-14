using FluentValidation;

namespace EShop.Ordering.Application.Orders.Commands.RemoveOrderItem;

public class RemoveOrderItemCommandValidator : AbstractValidator<RemoveOrderItemCommand>
{
    public RemoveOrderItemCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required");

        RuleFor(x => x.ItemId)
            .NotEmpty().WithMessage("Item ID is required");
    }
}
