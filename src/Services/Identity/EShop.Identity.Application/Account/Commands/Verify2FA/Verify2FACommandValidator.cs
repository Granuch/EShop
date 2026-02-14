using FluentValidation;

namespace EShop.Identity.Application.Account.Commands.Verify2FA;

/// <summary>
/// Validator for Verify2FACommand
/// </summary>
public class Verify2FACommandValidator : AbstractValidator<Verify2FACommand>
{
    public Verify2FACommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required")
            .Length(6).WithMessage("Verification code must be 6 digits")
            .Matches("^[0-9]+$").WithMessage("Verification code must contain only digits");
    }
}
