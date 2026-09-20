using FluentValidation;

namespace EShop.Ordering.Application.Orders.Queries.GetOrderNotes;

public class GetOrderNotesQueryValidator : AbstractValidator<GetOrderNotesQuery>
{
    public GetOrderNotesQueryValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required");
    }
}
