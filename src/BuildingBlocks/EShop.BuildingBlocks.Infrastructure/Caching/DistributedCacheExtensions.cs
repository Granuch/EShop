using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace EShop.BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// Extension methods for IDistributedCache to simplify working with typed objects
/// </summary>
public static class DistributedCacheExtensions
{
    // TODO: Implement GetAsync with JSON deserialization
    public static async Task<T?> GetAsync<T>(
        this IDistributedCache cache,
        string key,
        CancellationToken cancellationToken = default)
    {
        // TODO: Get byte array from cache
        // TODO: Deserialize to type T using System.Text.Json
        // TODO: Return default if not found
        throw new NotImplementedException();
    }

    // TODO: Implement SetAsync with JSON serialization
    public static async Task SetAsync<T>(
        this IDistributedCache cache,
        string key,
        T value,
        TimeSpan? absoluteExpiration = null,
        CancellationToken cancellationToken = default)
    {
        // TODO: Serialize value to JSON
        // TODO: Set expiration options
        // TODO: Store in cache
        throw new NotImplementedException();
    }

    // TODO: Implement RemoveAsync
    public static async Task RemoveAsync(
        this IDistributedCache cache,
        string key,
        CancellationToken cancellationToken = default)
    {
        // TODO: Remove key from cache
        throw new NotImplementedException();
    }
}
