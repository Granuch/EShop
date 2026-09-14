using FluentValidation;

namespace EShop.Ordering.Application.Orders.Commands.DeliverOrder;

public class DeliverOrderCommandValidator : AbstractValidator<DeliverOrderCommand>
{
    public DeliverOrderCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required");
    }
}
