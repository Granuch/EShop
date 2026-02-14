using FluentValidation;

namespace EShop.Identity.Application.Auth.Commands.RevokeToken;

/// <summary>
/// Validator for RevokeTokenCommand
/// </summary>
public class RevokeTokenCommandValidator : AbstractValidator<RevokeTokenCommand>
{
    public RevokeTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required");
    }
}
