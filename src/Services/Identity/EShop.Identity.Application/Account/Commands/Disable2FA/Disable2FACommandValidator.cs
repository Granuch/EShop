using FluentValidation;

namespace EShop.Identity.Application.Account.Commands.Disable2FA;

/// <summary>
/// Validator for Disable2FACommand
/// </summary>
public class Disable2FACommandValidator : AbstractValidator<Disable2FACommand>
{
    public Disable2FACommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required")
            .Length(6).WithMessage("Verification code must be 6 digits")
            .Matches("^[0-9]+$").WithMessage("Verification code must contain only digits");
    }
}
