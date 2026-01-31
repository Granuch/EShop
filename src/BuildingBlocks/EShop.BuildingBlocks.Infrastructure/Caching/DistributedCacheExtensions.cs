using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace EShop.BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// Extension methods for IDistributedCache to simplify working with typed objects
/// </summary>
public static class DistributedCacheExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Gets a value from cache and deserializes it to the specified type
    /// </summary>
    public static async Task<T?> GetAsync<T>(
        this IDistributedCache cache,
        string key,
        CancellationToken cancellationToken = default)
    {
        var bytes = await cache.GetAsync(key, cancellationToken);

        if (bytes == null || bytes.Length == 0)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
    }

    /// <summary>
    /// Serializes a value to JSON and stores it in cache
    /// </summary>
    public static async Task SetAsync<T>(
        this IDistributedCache cache,
        string key,
        T value,
        TimeSpan? absoluteExpiration = null,
        TimeSpan? slidingExpiration = null,
        CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions();

        if (absoluteExpiration.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = absoluteExpiration;
        }

        if (slidingExpiration.HasValue)
        {
            options.SlidingExpiration = slidingExpiration;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await cache.SetAsync(key, bytes, options, cancellationToken);
    }

    /// <summary>
    /// Gets a value from cache or creates it using the factory if not found
    /// </summary>
    public static async Task<T?> GetOrSetAsync<T>(
        this IDistributedCache cache,
        string key,
        Func<Task<T>> factory,
        TimeSpan? absoluteExpiration = null,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.GetAsync<T>(key, cancellationToken);

        if (cached != null)
        {
            return cached;
        }

        var value = await factory();

        if (value != null)
        {
            await cache.SetAsync(key, value, absoluteExpiration, cancellationToken: cancellationToken);
        }

        return value;
    }

    /// <summary>
    /// Removes a value from cache
    /// </summary>
    public static async Task RemoveAsync(
        this IDistributedCache cache,
        string key,
        CancellationToken cancellationToken = default)
    {
        await cache.RemoveAsync(key, cancellationToken);
    }

    /// <summary>
    /// Removes multiple values from cache by pattern prefix
    /// Note: This is a convenience method - for production use Redis SCAN
    /// </summary>
    public static async Task RemoveByPrefixAsync(
        this IDistributedCache cache,
        string prefix,
        IEnumerable<string> knownKeys,
        CancellationToken cancellationToken = default)
    {
        var keysToRemove = knownKeys.Where(k => k.StartsWith(prefix));

        foreach (var key in keysToRemove)
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
    }
}
