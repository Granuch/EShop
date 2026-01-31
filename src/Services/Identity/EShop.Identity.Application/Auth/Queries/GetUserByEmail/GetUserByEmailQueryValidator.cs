using FluentValidation;

namespace EShop.Identity.Application.Auth.Queries.GetUserByEmail;

/// <summary>
/// Validator for GetUserByEmailQuery
/// </summary>
public class GetUserByEmailQueryValidator : AbstractValidator<GetUserByEmailQuery>
{
    public GetUserByEmailQueryValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");
    }
}
