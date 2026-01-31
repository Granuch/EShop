using FluentValidation;

namespace EShop.Identity.Application.Account.Commands.Enable2FA;

/// <summary>
/// Validator for Enable2FACommand
/// </summary>
public class Enable2FACommandValidator : AbstractValidator<Enable2FACommand>
{
    public Enable2FACommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");
    }
}
