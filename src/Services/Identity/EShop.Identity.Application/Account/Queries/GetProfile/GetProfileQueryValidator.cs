using FluentValidation;

namespace EShop.Identity.Application.Account.Queries.GetProfile;

/// <summary>
/// Validator for GetProfileQuery
/// </summary>
public class GetProfileQueryValidator : AbstractValidator<GetProfileQuery>
{
    public GetProfileQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");
    }
}
