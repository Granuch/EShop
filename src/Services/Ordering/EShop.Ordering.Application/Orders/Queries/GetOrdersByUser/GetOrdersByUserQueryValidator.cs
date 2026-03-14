using FluentValidation;

namespace EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;

public class GetOrdersByUserQueryValidator : AbstractValidator<GetOrdersByUserQuery>
{
    public GetOrdersByUserQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");
    }
}
