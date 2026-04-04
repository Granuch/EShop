using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace EShop.Basket.Infrastructure.Caching;

public class CircuitBreakingDistributedCache : IDistributedCache
{
    private readonly IDistributedCache _inner;
    private readonly ILogger<CircuitBreakingDistributedCache> _logger;

    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;

    private int _failureCount;
    private DateTime _openUntil = DateTime.MinValue;
    private int _halfOpenProbeActive;
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
                    if (Interlocked.CompareExchange(ref _halfOpenProbeActive, 1, 0) == 0)
                    {
                        return false;
                    }

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
                    _openDuration.TotalSeconds,
                    _failureCount);
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
        catch
        {
            RecordFailure();
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
        catch
        {
            RecordFailure();
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
        catch
        {
            RecordFailure();
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
        catch
        {
            RecordFailure();
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
        catch
        {
            RecordFailure();
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
        catch
        {
            RecordFailure();
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
        catch
        {
            RecordFailure();
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
        catch
        {
            RecordFailure();
        }
    }
}
