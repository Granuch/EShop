using FluentValidation;

namespace EShop.Identity.Application.Users.Queries.GetUserContact;

public sealed class GetUserContactQueryValidator : AbstractValidator<GetUserContactQuery>
{
    public GetUserContactQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required");
    }
}
