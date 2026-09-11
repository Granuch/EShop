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
///
/// <para>
/// L28 (Catalog audit Stage 10). Three things changed, none of them the state machine itself.
/// The admission check was an <c>IsOpen</c> property whose getter <i>claimed the half-open probe</i>
/// — a read with a side effect, so a debugger watch or a log line reading it would have consumed the
/// probe; it is now the explicitly-named <see cref="TryAcquirePermission"/>. Time comes from an
/// injectable <see cref="TimeProvider"/>, which is what makes the cooldown testable. And an outage is
/// now visible: every per-call failure logged at Debug, so a Redis outage produced exactly one Warning
/// (the OPEN line) and then silence. The first failure of each streak is now a Warning with its
/// exception, and recovery logs CLOSED.
/// </para>
/// </summary>
public class CircuitBreakingDistributedCache : IDistributedCache
{
    private readonly IDistributedCache _inner;
    private readonly ILogger<CircuitBreakingDistributedCache> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;

    private int _failureCount;
    private DateTimeOffset _openUntil = DateTimeOffset.MinValue;
    private bool _probeInFlight;
    private readonly object _lock = new();

    public CircuitBreakingDistributedCache(
        IDistributedCache inner,
        ILogger<CircuitBreakingDistributedCache> logger,
        int failureThreshold = 3,
        TimeSpan? openDuration = null,
        TimeProvider? timeProvider = null)
    {
        _inner = inner;
        _logger = logger;
        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Whether this call may go to Redis. Closed: always. Open: never until the cooldown ends, then
    /// exactly one caller becomes the half-open probe and everyone else keeps skipping until that
    /// probe reports back through <see cref="RecordSuccess"/> or <see cref="RecordFailure"/>.
    /// </summary>
    private bool TryAcquirePermission()
    {
        lock (_lock)
        {
            if (_failureCount < _failureThreshold)
                return true;

            if (_timeProvider.GetUtcNow() < _openUntil || _probeInFlight)
                return false;

            _probeInFlight = true;
            return true;
        }
    }

    private void RecordSuccess()
    {
        lock (_lock)
        {
            if (_failureCount >= _failureThreshold)
            {
                _logger.LogInformation("Redis circuit breaker CLOSED. Cache calls resumed after a successful probe");
            }

            _failureCount = 0;
            _probeInFlight = false;
        }
    }

    private void RecordFailure(Exception exception, string operation, string key)
    {
        lock (_lock)
        {
            _failureCount++;
            _probeInFlight = false;

            // The first failure of a streak carries the exception at Warning, so an outage says why.
            // The rest stay at Debug: while the circuit is closed they can arrive once per request.
            if (_failureCount == 1)
            {
                _logger.LogWarning(exception, "Cache {Operation} failed for key {Key}", operation, key);
            }
            else
            {
                _logger.LogDebug(exception, "Cache {Operation} failed for key {Key}", operation, key);
            }

            if (_failureCount >= _failureThreshold)
            {
                _openUntil = _timeProvider.GetUtcNow().Add(_openDuration);
                _logger.LogWarning(
                    "Redis circuit breaker OPEN. Suppressing cache calls for {Duration}s after {Failures} consecutive failures",
                    _openDuration.TotalSeconds, _failureCount);
            }
        }
    }

    public byte[]? Get(string key)
    {
        if (!TryAcquirePermission()) return null;
        try
        {
            var result = _inner.Get(key);
            RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(ex, nameof(Get), key);
            return null;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        if (!TryAcquirePermission()) return null;
        try
        {
            var result = await _inner.GetAsync(key, token);
            RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            RecordFailure(ex, nameof(GetAsync), key);
            return null;
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        if (!TryAcquirePermission()) return;
        try
        {
            _inner.Set(key, value, options);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure(ex, nameof(Set), key);
        }
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        if (!TryAcquirePermission()) return;
        try
        {
            await _inner.SetAsync(key, value, options, token);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure(ex, nameof(SetAsync), key);
        }
    }

    public void Refresh(string key)
    {
        if (!TryAcquirePermission()) return;
        try
        {
            _inner.Refresh(key);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure(ex, nameof(Refresh), key);
        }
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        if (!TryAcquirePermission()) return;
        try
        {
            await _inner.RefreshAsync(key, token);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure(ex, nameof(RefreshAsync), key);
        }
    }

    public void Remove(string key)
    {
        if (!TryAcquirePermission()) return;
        try
        {
            _inner.Remove(key);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure(ex, nameof(Remove), key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        if (!TryAcquirePermission()) return;
        try
        {
            await _inner.RemoveAsync(key, token);
            RecordSuccess();
        }
        catch (Exception ex)
        {
            RecordFailure(ex, nameof(RemoveAsync), key);
        }
    }
}
