using FluentValidation;

namespace EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;

public class GetOrdersByUserQueryValidator : AbstractValidator<GetOrdersByUserQuery>
{
    public GetOrdersByUserQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.EffectivePageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1");

        RuleFor(x => x.EffectivePageSize)
            .GreaterThanOrEqualTo(1).WithMessage("Page size must be at least 1")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100");
    }
}
