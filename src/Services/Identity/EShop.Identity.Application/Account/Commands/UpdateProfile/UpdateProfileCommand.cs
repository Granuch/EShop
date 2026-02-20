using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Identity.Application.Account.Commands.UpdateProfile;

/// <summary>
/// Command to update user profile.
/// Implements ICacheInvalidatingCommand to invalidate the profile cache after update.
/// </summary>
public record UpdateProfileCommand : IRequest<Result<UpdateProfileResponse>>, ICacheInvalidatingCommand, ITransactionalCommand
{
    public string UserId { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? ProfilePictureUrl { get; init; }

    /// <summary>
    /// Invalidates the user's profile cache after successful update.
    /// Uses the same key format as GetProfileQuery.CacheKey.
    /// </summary>
    public IEnumerable<string> CacheKeysToInvalidate => [$"profile:{UserId}"];
}

public record UpdateProfileResponse
{
    public string Message { get; init; } = string.Empty;
}
