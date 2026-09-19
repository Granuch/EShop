using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Identity.Application.Validation;
using EShop.Identity.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Users.Commands.UpdateUserProfile;

/// <summary>
/// Edits another user's profile as an admin (Admin panel S7, endpoint #4).
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>UpdateProfileCommand</c>, which takes its user id from the caller's own token
/// and is reachable by every signed-in user. Reusing it would have meant an endpoint whose
/// authorization depends on which id the controller happens to pass.
/// </para>
/// <para>
/// <b>Every field follows the BUG-09 three-case rule</b>, including the two that are required on
/// the self-service version: omitted leaves the stored value, an empty string clears it where
/// clearing is meaningful, and a value replaces it. That is a deliberate difference — this is a
/// partial-update form an admin opens to fix one field, and making first/last name required would
/// mean an admin correcting a phone number has to re-send a name they never looked at.
/// </para>
/// <para>
/// <c>[SensitiveData]</c> on the phone number: <c>LoggingBehavior</c> logs the whole request at
/// Information and its name list covers credentials, not personal data. Same reasoning as Basket
/// audit S10's shipping address.
/// </para>
/// </remarks>
public record UpdateUserProfileCommand : IRequest<Result<Unit>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    [SensitiveData]
    public string? PhoneNumber { get; init; }

    public string? ProfilePictureUrl { get; init; }

    /// <summary>
    /// The key <c>GetProfileQuery</c> writes through <c>CachingBehavior</c>, so
    /// <c>CacheInvalidationBehavior</c> rewrites it identically and the eviction actually lands.
    /// Contrast <c>user_roles:{id}</c>, which <c>CachedUserRolesService</c> owns and this marker
    /// cannot touch — see <c>SetUserRolesCommand</c>.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public class UpdateUserProfileCommandValidator : AbstractValidator<UpdateUserProfileCommand>
{
    public UpdateUserProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User ID is required");

        // A name may be omitted, but not blanked: a user with no surname is not a state any other
        // path can produce. The trailing .When is what makes "omitted" legal and is load-bearing —
        // deleting it 400s every partial update.
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name must not be blank")
            .MaximumLength(PersonNameRules.MaxLength)
                .WithMessage($"First name must not exceed {PersonNameRules.MaxLength} characters")
            .Matches(PersonNameRules.Pattern).WithMessage($"First name {PersonNameRules.Message}")
            .When(x => x.FirstName is not null);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name must not be blank")
            .MaximumLength(PersonNameRules.MaxLength)
                .WithMessage($"Last name must not exceed {PersonNameRules.MaxLength} characters")
            .Matches(PersonNameRules.Pattern).WithMessage($"Last name {PersonNameRules.Message}")
            .When(x => x.LastName is not null);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(32).WithMessage("Phone number must not exceed 32 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));

        RuleFor(x => x.ProfilePictureUrl)
            .MaximumLength(500).WithMessage("Profile picture URL must not exceed 500 characters")
            .Must(BeAValidUrl).WithMessage("Profile picture URL must be a valid URL")
            .When(x => !string.IsNullOrEmpty(x.ProfilePictureUrl));
    }

    private static bool BeAValidUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var result)
               && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
    }
}

public class UpdateUserProfileCommandHandler : IRequestHandler<UpdateUserProfileCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<UpdateUserProfileCommandHandler> _logger;

    public UpdateUserProfileCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<UpdateUserProfileCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(UpdateUserProfileCommand request, CancellationToken cancellationToken)
    {
        // FindByIdAsync runs under the !IsDeleted filter, which is the behaviour every write path
        // wants: a deleted account is edited by restoring it first.
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user is null)
        {
            return Result<Unit>.Failure(AdminUserErrors.NotFound);
        }

        if (request.FirstName is not null)
            user.FirstName = request.FirstName.Trim();

        if (request.LastName is not null)
            user.LastName = request.LastName.Trim();

        // Blank clears, for the two fields where "no value" is a real state.
        if (request.PhoneNumber is not null)
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        if (request.ProfilePictureUrl is not null)
            user.ProfilePictureUrl = string.IsNullOrWhiteSpace(request.ProfilePictureUrl) ? null : request.ProfilePictureUrl;

        AdminUserErrors.EnsureSucceededAfterMutation(
            await _userManager.UpdateAsync(user), "Updating the user profile");

        _logger.LogInformation("Admin updated user profile. UserId={UserId}", request.UserId);

        return Result<Unit>.Success(Unit.Value);
    }
}
