using FluentValidation;

namespace EShop.Identity.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Validator for ForgotPasswordCommand
/// </summary>
public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters");
    }
}
