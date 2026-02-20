using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Infrastructure.Caching;

/// <summary>
/// Decorates IDistributedCache with circuit breaker behavior.
/// When Redis fails repeatedly, this skips cache calls for a cooldown period
/// to avoid stacking 5-second timeouts on every request.
///
/// States:
///   Closed   → normal operation, all calls go to Redis
///   Open     → Redis is assumed down, all calls return null / no-op
///   HalfOpen → after cooldown expires, one probe call goes through
///
/// This is an in-process circuit breaker. In a multi-instance deployment,
/// each instance maintains its own state. This is acceptable because:
///   1. Redis failures are typically cluster-wide
///   2. Each instance recovers independently
///   3. No external coordination needed
/// </summary>
public class CircuitBreakingDistributedCache : IDistributedCache
{
    private readonly IDistributedCache _inner;
    private readonly ILogger<CircuitBreakingDistributedCache> _logger;

    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;

    private int _failureCount;
    private DateTime _openUntil = DateTime.MinValue;
    private int _halfOpenProbeActive; // 0 = no probe, 1 = probe in progress
    private readonly object _lock = new();

    public CircuitBreakingDistributedCache(
        IDistributedCache inner,
        ILogger<CircuitBreakingDistributedCache> logger,
        int failureThreshold = 3,
        TimeSpan? openDuration = null)
    {
        _inner = inner;
        _logger = logger;
        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
    }

    private bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                if (_failureCount < _failureThreshold)
                    return false;

                if (DateTime.UtcNow >= _openUntil)
                {
                    // Half-open: allow exactly one probe via Interlocked
                    if (Interlocked.CompareExchange(ref _halfOpenProbeActive, 1, 0) == 0)
                    {
                        return false; // this thread is the probe
                    }
                    // Another thread is already probing, stay open for this one
                    return true;
                }

                return true;
            }
        }
    }

    private void RecordSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            Interlocked.Exchange(ref _halfOpenProbeActive, 0);
        }
    }

    private void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            Interlocked.Exchange(ref _halfOpenProbeActive, 0);
            if (_failureCount >= _failureThreshold)
            {
                _openUntil = DateTime.UtcNow.Add(_openDuration);
                _logger.LogWarning(
                    "Redis circuit breaker OPEN. Suppressing cache calls for {Duration}s after {Failures} consecutive failures",
                    _openDuration.TotalSeconds, _failureCount);
            }
        }
    }

    public byte[]? Get(string key)
    {
        if (IsOpen) return null;
        try
        {
            var result = _inner.Get(key);
            RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogDebug(ex, "Cache Get failed for key {Key}", key);
            return null;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        if (IsOpen) return null;
        try
        {
            var result = await _inner.GetAsync(key, token);
            RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogDebug(ex, "Cache GetAsync failed for key {Key}", key);
            return null;
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        if (IsOpen) return;
        try
        {
            _inner.Set(key, value, options);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogDebug(ex, "Cache Set failed for key {Key}", key);
        }
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        if (IsOpen) return;
        try
        {
            await _inner.SetAsync(key, value, options, token);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogDebug(ex, "Cache SetAsync failed for key {Key}", key);
        }
    }

    public void Refresh(string key)
    {
        if (IsOpen) return;
        try
        {
            _inner.Refresh(key);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogDebug(ex, "Cache Refresh failed for key {Key}", key);
        }
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        if (IsOpen) return;
        try
        {
            await _inner.RefreshAsync(key, token);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogDebug(ex, "Cache RefreshAsync failed for key {Key}", key);
        }
    }

    public void Remove(string key)
    {
        if (IsOpen) return;
        try
        {
            _inner.Remove(key);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogDebug(ex, "Cache Remove failed for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        if (IsOpen) return;
        try
        {
            await _inner.RemoveAsync(key, token);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogDebug(ex, "Cache RemoveAsync failed for key {Key}", key);
        }
    }
}
