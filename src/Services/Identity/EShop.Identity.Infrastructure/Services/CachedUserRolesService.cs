using EShop.BuildingBlocks.Infrastructure.Caching;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Infrastructure.Services;

/// <summary>
/// Implementation of cached user roles service using Redis distributed cache.
/// The interface lives in <c>EShop.Identity.Domain.Interfaces</c> so the Application layer's
/// role handlers can invalidate this cache without Application referencing Infrastructure.
/// </summary>
public class CachedUserRolesService : ICachedUserRolesService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedUserRolesService> _logger;

    private const string CacheKeyPrefix = "user_roles:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public CachedUserRolesService(
        UserManager<ApplicationUser> userManager,
        IDistributedCache cache,
        ILogger<CachedUserRolesService> logger)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return await GetRolesByUserIdAsync(user.Id, cancellationToken);
    }

    public async Task<IList<string>> GetRolesByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var cacheKey = GetCacheKey(userId);

        try
        {
            var cachedRoles = await _cache.GetOrSetAsync(
                cacheKey,
                async () =>
                {
                    var user = await _userManager.FindByIdAsync(userId);
                    if (user == null)
                    {
                        _logger.LogWarning("User not found when fetching roles. UserId={UserId}", userId);
                        return new List<string>();
                    }

                    var roles = await _userManager.GetRolesAsync(user);
                    _logger.LogDebug("Fetched roles from database. UserId={UserId}, Roles={Roles}", 
                        userId, string.Join(",", roles));
                    return roles.ToList();
                },
                CacheDuration,
                cancellationToken);

            return cachedRoles ?? new List<string>();
        }
        catch (Exception ex)
        {
            // Cache failure should not break the application
            _logger.LogWarning(ex, "Cache operation failed for user roles. UserId={UserId}. Falling back to database.", userId);

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return new List<string>();
            }

            return await _userManager.GetRolesAsync(user);
        }
    }

    public async Task InvalidateRolesCacheAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var cacheKey = GetCacheKey(userId);

        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            _logger.LogDebug("Invalidated roles cache. UserId={UserId}", userId);
        }
        catch (Exception ex)
        {
            // Cache failure should not break the application
            _logger.LogWarning(ex, "Failed to invalidate roles cache. UserId={UserId}", userId);
        }
    }

    private static string GetCacheKey(string userId) => $"{CacheKeyPrefix}{userId}";
}
