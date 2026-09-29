using FluentValidation;

namespace EShop.Identity.Application.Auth.Commands.ResendEmailConfirmation;

/// <summary>
/// The same email rules as forgot-password: the only answer that differs from the uniform 200 is a
/// malformed request, which reveals nothing about any account.
/// </summary>
public class ResendEmailConfirmationCommandValidator : AbstractValidator<ResendEmailConfirmationCommand>
{
    public ResendEmailConfirmationCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters");
    }
}
