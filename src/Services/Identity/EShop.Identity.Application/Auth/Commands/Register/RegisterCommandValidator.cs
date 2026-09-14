using EShop.Identity.Application.Validation;
using FluentValidation;

namespace EShop.Identity.Application.Auth.Commands.Register;

/// <summary>
/// Validator for registration command
/// </summary>
public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(100).WithMessage("Password must not exceed 100 characters")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at least one special character");

        // Name rules come from PersonNameRules so this validator and UpdateProfileCommandValidator
        // cannot drift — they used to hold four separate copies of the same regex.
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required")
            .MaximumLength(PersonNameRules.MaxLength)
                .WithMessage($"First name must not exceed {PersonNameRules.MaxLength} characters")
            .Matches(PersonNameRules.Pattern).WithMessage($"First name {PersonNameRules.Message}");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required")
            .MaximumLength(PersonNameRules.MaxLength)
                .WithMessage($"Last name must not exceed {PersonNameRules.MaxLength} characters")
            .Matches(PersonNameRules.Pattern).WithMessage($"Last name {PersonNameRules.Message}");
    }
}
