using FluentValidation;

namespace EShop.Identity.Application.Auth.Commands.ResetPassword;

/// <summary>
/// Validator for ResetPasswordCommand
/// </summary>
public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Reset token is required");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character");
    }
}
