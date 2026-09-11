using FluentValidation;

namespace EShop.Ordering.Application.Orders.Queries.GetOrdersByUser;

public class GetOrdersByUserQueryValidator : AbstractValidator<GetOrdersByUserQuery>
{
    public GetOrdersByUserQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        // Named as sent: a client never sees an "EffectivePageSize" parameter.
        RuleFor(x => x.EffectivePageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1")
            .OverridePropertyName(nameof(GetOrdersByUserQuery.PageNumber));

        RuleFor(x => x.EffectivePageSize)
            .GreaterThanOrEqualTo(1).WithMessage("Page size must be at least 1")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100")
            .OverridePropertyName(nameof(GetOrdersByUserQuery.PageSize));
    }
}
