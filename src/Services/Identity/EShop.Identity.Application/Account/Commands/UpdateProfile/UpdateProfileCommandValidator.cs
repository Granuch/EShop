using EShop.Identity.Application.Validation;
using FluentValidation;

namespace EShop.Identity.Application.Account.Commands.UpdateProfile;

/// <summary>
/// Validator for update profile command
/// </summary>
public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        // Shared with RegisterCommandValidator via PersonNameRules: a name that can be registered
        // must remain editable, so these two must never disagree.
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

        RuleFor(x => x.ProfilePictureUrl)
            .MaximumLength(500).WithMessage("Profile picture URL must not exceed 500 characters")
            .Must(BeAValidUrl).WithMessage("Profile picture URL must be a valid URL")
            .When(x => !string.IsNullOrEmpty(x.ProfilePictureUrl));
    }

    private bool BeAValidUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var result)
               && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
    }
}
