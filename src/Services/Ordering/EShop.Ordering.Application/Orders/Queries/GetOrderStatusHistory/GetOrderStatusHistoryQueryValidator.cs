using FluentValidation;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderStatusHistory;

public class GetOrderStatusHistoryQueryValidator : AbstractValidator<GetOrderStatusHistoryQuery>
{
    public GetOrderStatusHistoryQueryValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required");
    }
}
