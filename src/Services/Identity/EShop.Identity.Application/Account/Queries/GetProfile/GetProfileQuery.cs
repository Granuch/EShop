using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;

namespace EShop.Identity.Application.Account.Queries.GetProfile;

/// <summary>
/// Query to get user profile with automatic caching.
/// Cache key: "profile:{userId}"
/// Cache duration: 5 minutes (absolute)
/// Invalidated by: UpdateProfileCommand, Enable2FACommand, Disable2FACommand
/// </summary>
public record GetProfileQuery : IRequest<Result<UserProfileResponse>>, ICacheableQuery
{
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Cache key format: "profile:{userId}"
    /// Uses user ID which is safe to include in cache keys.
    /// </summary>
    public string CacheKey => $"profile:{UserId}";

    /// <summary>
    /// Profile data cached for 5 minutes to balance freshness vs performance.
    /// Short enough to reflect changes reasonably quickly.
    /// </summary>
    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);

    /// <summary>
    /// No sliding expiration - use absolute only for predictable cache behavior.
    /// </summary>
    public TimeSpan? SlidingExpiration => null;
}

public record UserProfileResponse
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? ProfilePictureUrl { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public List<string> Roles { get; init; } = [];
}
