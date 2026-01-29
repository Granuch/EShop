using FluentValidation;

namespace EShop.Identity.Application.Auth.Commands.Login;

/// <summary>
/// Validator for login command
/// </summary>
public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required");

        RuleFor(x => x.TwoFactorCode)
            .Length(6).WithMessage("Two-factor code must be 6 digits")
            .Matches("^[0-9]+$").WithMessage("Two-factor code must contain only digits")
            .When(x => !string.IsNullOrEmpty(x.TwoFactorCode));
    }
}
