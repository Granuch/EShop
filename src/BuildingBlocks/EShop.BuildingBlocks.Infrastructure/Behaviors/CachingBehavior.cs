using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Infrastructure.Caching;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Collections.Concurrent;

namespace EShop.BuildingBlocks.Infrastructure.Behaviors;

/// <summary>
/// MediatR pipeline behavior that provides automatic caching for queries.
/// 
/// Features:
/// - Only caches requests that implement ICacheableQuery
/// - Uses distributed cache (Redis in production, in-memory for testing)
/// - Configurable expiration (absolute and sliding)
/// - Cache stampede prevention via locking
/// - Versioned cache keys for easy invalidation
/// - Safe serialization with proper error handling
/// - Smart Result<T> unwrapping: caches only payload, not the wrapper
/// 
/// Usage:
/// 1. Implement ICacheableQuery on your query class
/// 2. Define the CacheKey property with query parameters
/// 3. Optionally set CacheDuration and SlidingExpiration
/// 
/// Result<T> Support:
/// - If TResponse is Result<T>, only the payload (T) is cached
/// - On cache hit: returns Result<T>.Success(cachedPayload)
/// - On failure: Result<T>.Failure(...) is NOT cached
/// </summary>
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> GetAsyncMethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> SetAsyncMethodCache = new();

    private readonly IDistributedCache _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;
    private readonly CachingBehaviorOptions _options;
    private readonly ICacheKeyVersionProvider? _versionProvider;

    public CachingBehavior(
        IDistributedCache cache,
        ILogger<CachingBehavior<TRequest, TResponse>> logger,
        IOptions<CachingBehaviorOptions>? options = null,
        ICacheKeyVersionProvider? versionProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new CachingBehaviorOptions();

        // Optional so a service that caches nothing versioned needs no extra registration; a
        // query marked IVersionedCacheKey in a host without one simply keys as it did before.
        _versionProvider = versionProvider;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only cache requests that implement ICacheableQuery
        if (request is not ICacheableQuery cacheableQuery)
        {
            return await next();
        }

        var cacheKey = await BuildCacheKeyAsync(cacheableQuery, cancellationToken);
        var requestName = typeof(TRequest).Name;

        // Check if TResponse is Result<T>
        var isResultType = IsResultType(typeof(TResponse), out var payloadType);

        try
        {
            // Try to get from cache first
            if (isResultType && payloadType != null)
            {
                // TResponse is Result<T>, cache only the payload (T)
                var cachedPayload = await GetCachedPayloadAsync(cacheKey, payloadType, cancellationToken);
                if (cachedPayload != null)
                {
                    _logger.LogDebug(
                        "Cache hit for {RequestName} with key {CacheKey}. Wrapping payload in Result<T>",
                        requestName, cacheKey);

                    // Wrap payload in Result<T>.Success()
                    var resultResponse = CreateSuccessResult(payloadType, cachedPayload);
                    return (TResponse)resultResponse!;
                }
            }
            else
            {
                // TResponse is NOT Result<T>, cache the whole response
                var cachedResponse = await _cache.GetAsync<TResponse>(cacheKey, cancellationToken);

                if (cachedResponse != null)
                {
                    _logger.LogDebug(
                        "Cache hit for {RequestName} with key {CacheKey}",
                        requestName, cacheKey);

                    return cachedResponse;
                }
            }

            _logger.LogDebug(
                "Cache miss for {RequestName} with key {CacheKey}",
                requestName, cacheKey);
        }
        catch (Exception ex)
        {
            // Cache failures should not break the application
            _logger.LogWarning(ex,
                "Failed to read from cache for {RequestName} with key {CacheKey}. Proceeding without cache",
                requestName, cacheKey);
        }

        // Execute the handler
        var response = await next();

        // Cache the response if it's not null
        if (response != null)
        {
            try
            {
                var duration = cacheableQuery.CacheDuration ?? _options.DefaultDuration;
                var sliding = cacheableQuery.SlidingExpiration;

                if (isResultType && payloadType != null)
                {
                    // TResponse is Result<T>
                    // Only cache if Result.IsSuccess == true
                    var isSuccess = GetIsSuccessProperty(response);
                    if (isSuccess)
                    {
                        var payload = GetValueProperty(response, payloadType);
                        if (payload != null)
                        {
                            await SetCachedPayloadAsync(
                                cacheKey,
                                payload,
                                payloadType,
                                duration,
                                sliding,
                                cancellationToken);

                            _logger.LogDebug(
                                "Cached Result<T> payload for {RequestName} with key {CacheKey}. Duration: {Duration}",
                                requestName, cacheKey, duration);
                        }
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Skipping cache for {RequestName} with key {CacheKey} because Result.IsSuccess = false",
                            requestName, cacheKey);
                    }
                }
                else
                {
                    // TResponse is NOT Result<T>, cache the whole response
                    await _cache.SetAsync(
                        cacheKey,
                        response,
                        absoluteExpiration: duration,
                        slidingExpiration: sliding,
                        cancellationToken: cancellationToken);

                    _logger.LogDebug(
                        "Cached response for {RequestName} with key {CacheKey}. Duration: {Duration}",
                        requestName, cacheKey, duration);
                }
            }
            catch (Exception ex)
            {
                // Cache failures should not break the application
                _logger.LogWarning(ex,
                    "Failed to write to cache for {RequestName} with key {CacheKey}",
                    requestName, cacheKey);
            }
        }

        return response;
    }

    /// <summary>
    /// Composes the stored key. The optional family segment (DEBT-16) is what makes a query family
    /// whose keys cannot be enumerated still invalidatable — see <see cref="IVersionedCacheKey"/>.
    /// A query that does not implement it, or a host with no
    /// <see cref="ICacheKeyVersionProvider"/> registered, keys exactly as before.
    /// </summary>
    private async Task<string> BuildCacheKeyAsync(ICacheableQuery query, CancellationToken cancellationToken)
    {
        var baseKey = query.CacheKey;

        if (query is IVersionedCacheKey versioned && _versionProvider is not null)
        {
            var familyVersion = await _versionProvider.GetVersionAsync(
                versioned.CacheKeyFamily, cancellationToken);
            baseKey = $"{versioned.CacheKeyFamily}@{familyVersion}:{baseKey}";
        }

        if (_options.UseVersioning)
        {
            return $"{_options.KeyPrefix}{_options.Version}:{baseKey}";
        }

        return $"{_options.KeyPrefix}{baseKey}";
    }

    /// <summary>
    /// Checks if type is Result<T> and extracts T
    /// </summary>
    private static bool IsResultType(Type type, out Type? payloadType)
    {
        payloadType = null;

        if (!type.IsGenericType)
            return false;

        var genericTypeDef = type.GetGenericTypeDefinition();
        if (genericTypeDef != typeof(Result<>))
            return false;

        payloadType = type.GetGenericArguments()[0];
        return true;
    }

    /// <summary>
    /// Gets payload from cache (using reflection to handle generic T)
    /// </summary>
    private async Task<object?> GetCachedPayloadAsync(string cacheKey, Type payloadType, CancellationToken cancellationToken)
    {
        // Call: await _cache.GetAsync<T>(cacheKey, cancellationToken)
        var method = GetAsyncMethodCache.GetOrAdd(payloadType, static type =>
            typeof(Caching.DistributedCacheExtensions)
                .GetMethod(nameof(Caching.DistributedCacheExtensions.GetAsync), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(type));

        var task = (Task)method.Invoke(null, new object[] { _cache, cacheKey, cancellationToken })!;
        await task.ConfigureAwait(false);

        var resultProperty = task.GetType().GetProperty("Result")!;
        return resultProperty.GetValue(task);
    }

    /// <summary>
    /// Sets payload to cache (using reflection to handle generic T)
    /// </summary>
    private async Task SetCachedPayloadAsync(
        string cacheKey,
        object payload,
        Type payloadType,
        TimeSpan duration,
        TimeSpan? sliding,
        CancellationToken cancellationToken)
    {
        // Call: await _cache.SetAsync<T>(cacheKey, payload, duration, sliding, cancellationToken)
        var method = SetAsyncMethodCache.GetOrAdd(payloadType, static type =>
            typeof(Caching.DistributedCacheExtensions)
                .GetMethod(nameof(Caching.DistributedCacheExtensions.SetAsync), BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(type));

        var task = (Task)method.Invoke(null, new object?[] { _cache, cacheKey, payload, duration, sliding, cancellationToken })!;
        await task.ConfigureAwait(false);
    }

    /// <summary>
    /// Creates Result<T>.Success(payload) using reflection
    /// </summary>
    private static object? CreateSuccessResult(Type payloadType, object payload)
    {
        // Call: Result<T>.Success(payload)
        var resultType = typeof(Result<>).MakeGenericType(payloadType);
        var successMethod = resultType.GetMethod("Success", BindingFlags.Public | BindingFlags.Static)!;
        return successMethod.Invoke(null, new[] { payload });
    }

    /// <summary>
    /// Gets Result<T>.IsSuccess property value
    /// </summary>
    private static bool GetIsSuccessProperty(object result)
    {
        var isSuccessProperty = result.GetType().GetProperty("IsSuccess")!;
        return (bool)isSuccessProperty.GetValue(result)!;
    }

    /// <summary>
    /// Gets Result<T>.Value property value
    /// </summary>
    private static object? GetValueProperty(object result, Type payloadType)
    {
        var valueProperty = result.GetType().GetProperty("Value")!;
        return valueProperty.GetValue(result);
    }
}

/// <summary>
/// MediatR pipeline behavior that invalidates cache entries when commands execute.
/// Works in conjunction with CachingBehavior.
///
/// <para>
/// Usage: implement <see cref="ICacheInvalidatingCommand"/> on your command and declare the exact
/// keys and/or the versioned key families it must evict, or add them to
/// <see cref="ICacheInvalidationContext"/> from the handler when they are only known after loading
/// domain data.
/// </para>
///
/// <para>
/// <b>This behavior must be registered OUTSIDE <c>TransactionBehavior</c>, and that is the whole
/// point of <c>AddEShopCacheInvalidation()</c>.</b> It invalidates after <c>await next()</c>
/// returns, so when it sits inside the transaction it evicts — and bumps family versions — while
/// the write is still uncommitted and invisible to everyone else. A concurrent read landing in
/// that window repopulates the cache with pre-commit data, under the *new* family version, where
/// it survives the full TTL. That is strictly worse than not invalidating at all, because the
/// bump that was supposed to fix staleness is what makes the stale entry addressable. Registering
/// this behavior before <c>Add&lt;Service&gt;Application()</c> puts it outside, so it runs after
/// the commit.
/// </para>
///
/// <para>
/// Two consequences of being outside, both intended: a handler that <i>throws</i> skips
/// invalidation entirely (correct — nothing committed), while a handler returning a
/// <c>Result</c> failure still commits, per <c>TransactionBehavior</c>'s documented behaviour, and
/// therefore still invalidates (also correct).
/// </para>
/// </summary>
public class CacheInvalidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CacheInvalidationBehavior<TRequest, TResponse>> _logger;
    private readonly CachingBehaviorOptions _options;
    private readonly ICacheInvalidationContext? _cacheInvalidationContext;
    private readonly ICacheKeyVersionProvider? _versionProvider;

    public CacheInvalidationBehavior(
        IDistributedCache cache,
        ILogger<CacheInvalidationBehavior<TRequest, TResponse>> logger,
        IOptions<CachingBehaviorOptions>? options = null,
        ICacheInvalidationContext? cacheInvalidationContext = null,
        ICacheKeyVersionProvider? versionProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new CachingBehaviorOptions();
        _cacheInvalidationContext = cacheInvalidationContext;
        // Optional: only services using IVersionedCacheKey register a provider. Declaring a family
        // without one is a no-op plus a warning, matching how a wildcard key behaves.
        _versionProvider = versionProvider;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Execute the handler first
        var response = await next();

        // Only invalidate cache for commands that implement ICacheInvalidatingCommand
        if (request is not ICacheInvalidatingCommand invalidatingCommand)
        {
            return response;
        }

        var requestName = typeof(TRequest).Name;

        var keysToInvalidate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (invalidatingCommand.CacheKeysToInvalidate is not null)
        {
            keysToInvalidate.UnionWith(invalidatingCommand.CacheKeysToInvalidate);
        }

        var familiesToInvalidate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (invalidatingCommand.CacheFamiliesToInvalidate is not null)
        {
            familiesToInvalidate.UnionWith(invalidatingCommand.CacheFamiliesToInvalidate);
        }

        if (_cacheInvalidationContext is not null)
        {
            keysToInvalidate.UnionWith(_cacheInvalidationContext.GetKeys());
            familiesToInvalidate.UnionWith(_cacheInvalidationContext.GetFamilies());
            _cacheInvalidationContext.Clear();
        }

        await InvalidateFamiliesAsync(familiesToInvalidate, requestName, cancellationToken);

        if (keysToInvalidate.Count == 0)
        {
            return response;
        }

        foreach (var keyPattern in keysToInvalidate)
        {
            try
            {
                var fullKey = $"{_options.KeyPrefix}{_options.Version}:{keyPattern}";
                
                // For exact keys, remove directly
                if (!keyPattern.Contains('*'))
                {
                    await _cache.RemoveAsync(fullKey, cancellationToken);
                    _logger.LogDebug(
                        "Invalidated cache key {CacheKey} after {RequestName}",
                        fullKey, requestName);
                }
                else
                {
                    // For pattern-based invalidation, log a warning
                    // Pattern-based invalidation requires Redis SCAN which is not available
                    // through IDistributedCache. Consider using IConnectionMultiplexer directly
                    // or implementing a key registry pattern.
                    _logger.LogWarning(
                        "Pattern-based cache invalidation '{Pattern}' requested by {RequestName} " +
                        "but not supported by IDistributedCache. Consider implementing key registry pattern",
                        keyPattern, requestName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to invalidate cache key {KeyPattern} after {RequestName}",
                    keyPattern, requestName);
            }
        }

        return response;
    }

    /// <summary>
    /// Bumps each declared family's version, making every key currently in it unreachable in one
    /// operation. Nothing is deleted — entries lapse on their own TTL — so a bump is not observable
    /// as absent keys; check the version entry instead.
    /// </summary>
    private async Task InvalidateFamiliesAsync(
        HashSet<string> families,
        string requestName,
        CancellationToken cancellationToken)
    {
        if (families.Count == 0)
        {
            return;
        }

        if (_versionProvider is null)
        {
            _logger.LogWarning(
                "Cache families {Families} requested by {RequestName} but no ICacheKeyVersionProvider "
                + "is registered — nothing was invalidated. Register one in the service's "
                + "Infrastructure extension.",
                string.Join(", ", families), requestName);
            return;
        }

        foreach (var family in families)
        {
            try
            {
                await _versionProvider.BumpVersionAsync(family, cancellationToken);
                _logger.LogDebug(
                    "Invalidated cache family {Family} after {RequestName}", family, requestName);
            }
            catch (Exception ex)
            {
                // Same posture as an exact key below: a cache failure must not fail a write that
                // has already committed. The cost is a stale family for up to its TTL.
                _logger.LogWarning(ex,
                    "Failed to invalidate cache family {Family} after {RequestName}",
                    family, requestName);
            }
        }
    }
}
