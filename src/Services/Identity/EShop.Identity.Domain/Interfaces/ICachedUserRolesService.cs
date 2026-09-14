using EShop.Identity.Domain.Entities;

namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Provides cached access to user roles to reduce database queries.
///
/// Cache strategy:
/// - Key format: "user_roles:{userId}"
/// - TTL: 5 minutes (absolute expiration)
/// - Invalidation: on role assignment/removal, or when the user logs out
///
/// Thread safety: the implementation uses cache-stampede prevention from
/// DistributedCacheExtensions.
///
/// <para>
/// <b>Why this interface lives in Domain rather than beside its implementation.</b> The role
/// membership handlers need to invalidate this cache, and Application does not (and should not)
/// reference Infrastructure. Declaring the abstraction here and implementing it in Infrastructure
/// is the same shape as <see cref="IRefreshTokenRepository"/> and <see cref="ITokenService"/>.
/// </para>
///
/// <para>
/// <b>This cache is NOT reachable via <c>ICacheInvalidatingCommand</c>.</b> That marker is
/// handled by <c>CacheInvalidationBehavior</c>, which rewrites every key as
/// <c>{KeyPrefix}{Version}:{key}</c> — it can only remove entries that <c>CachingBehavior</c>
/// itself wrote. This service manages its own unprefixed namespace, so a command marked with
/// the key "user_roles:{userId}" would remove a key that nothing ever wrote and report success.
/// Invalidate through <see cref="InvalidateRolesCacheAsync"/> instead.
/// </para>
/// </summary>
public interface ICachedUserRolesService
{
    /// <summary>
    /// Gets the roles for a user, using cache when available.
    /// </summary>
    Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the roles for a user by ID, using cache when available.
    /// </summary>
    Task<IList<string>> GetRolesByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the cached roles for a user.
    /// Call this after role assignment or removal.
    /// </summary>
    Task InvalidateRolesCacheAsync(string userId, CancellationToken cancellationToken = default);
}
