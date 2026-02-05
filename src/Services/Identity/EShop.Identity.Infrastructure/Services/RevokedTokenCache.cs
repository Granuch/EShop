using EShop.BuildingBlocks.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Infrastructure.Services;

/// <summary>
/// Service for caching revoked tokens to avoid database lookups on every request.
/// 
/// Security Model:
/// - When a token is revoked, it's added to the cache with TTL = token's original expiry time
/// - Token validation checks this cache first before querying the database
/// - Cache miss falls back to database lookup
/// 
/// Cache Strategy:
/// - Key format: "revoked_token:{tokenHash}" (hash of token, not the token itself)
/// - TTL: Matches the original token expiration time
/// - No sliding expiration (security requirement)
/// </summary>
public interface IRevokedTokenCache
{
    /// <summary>
    /// Checks if a refresh token has been revoked (cached check).
    /// Returns true if the token is in the revoked cache.
    /// Returns false if not in cache (may still need DB check for cache miss).
    /// </summary>
    Task<bool?> IsTokenRevokedAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a revoked token to the cache.
    /// Call this when a token is revoked.
    /// </summary>
    /// <param name="token">The token that was revoked</param>
    /// <param name="expiresAt">The original expiration time of the token</param>
    Task AddRevokedTokenAsync(string token, DateTime expiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a token from the revoked cache (if it was incorrectly added).
    /// Rarely needed.
    /// </summary>
    Task RemoveFromRevokedCacheAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>
/// Redis-backed implementation of revoked token cache.
/// </summary>
public class RevokedTokenCache : IRevokedTokenCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RevokedTokenCache> _logger;

    private const string CacheKeyPrefix = "revoked_token:";
    private const string RevokedMarker = "1"; // Simple marker to indicate revoked

    public RevokedTokenCache(
        IDistributedCache cache,
        ILogger<RevokedTokenCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool?> IsTokenRevokedAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true; // Empty token is considered invalid/revoked
        }

        var cacheKey = GetCacheKey(token);

        try
        {
            var cachedValue = await _cache.GetStringAsync(cacheKey, cancellationToken);
            
            if (cachedValue == null)
            {
                // Cache miss - caller should check database
                return null;
            }

            // Token is in revoked cache
            _logger.LogDebug("Token found in revoked cache");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check revoked token cache. Falling back to database check.");
            return null; // Indicate cache miss, caller should check database
        }
    }

    public async Task AddRevokedTokenAsync(string token, DateTime expiresAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var cacheKey = GetCacheKey(token);

        // Calculate TTL based on token's original expiration
        // We only need to keep the token in cache until it would have expired anyway
        var ttl = expiresAt - DateTime.UtcNow;

        if (ttl <= TimeSpan.Zero)
        {
            // Token has already expired, no need to cache
            _logger.LogDebug("Token already expired, skipping revoked cache entry");
            return;
        }

        // Cap the TTL at 30 days for safety (prevent indefinite storage)
        ttl = ttl > TimeSpan.FromDays(30) ? TimeSpan.FromDays(30) : ttl;

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                RevokedMarker,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                },
                cancellationToken);

            _logger.LogDebug("Added token to revoked cache. TTL={TtlMinutes} minutes", ttl.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add token to revoked cache. Token revocation still valid in database.");
        }
    }

    public async Task RemoveFromRevokedCacheAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var cacheKey = GetCacheKey(token);

        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            _logger.LogDebug("Removed token from revoked cache");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove token from revoked cache");
        }
    }

    /// <summary>
    /// Creates a cache key using a hash of the token.
    /// We don't store the actual token in the key for security.
    /// </summary>
    private static string GetCacheKey(string token)
    {
        // Use a hash of the token for the cache key
        // This prevents the actual token from appearing in logs or cache management tools
        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(token)));

        // Take first 16 chars of hash (sufficient for uniqueness)
        return $"{CacheKeyPrefix}{hash[..16]}";
    }
}
