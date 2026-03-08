using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace EShop.BuildingBlocks.Infrastructure.Observability;

/// <summary>
/// Wraps a <see cref="BaseExporter{T}"/> with circuit breaker behavior.
/// When the inner exporter fails repeatedly, this exporter short-circuits
/// and drops traces for a cooldown period to prevent log spam and wasted CPU.
///
/// States:
///   Closed   → normal operation, all batches go to inner exporter
///   Open     → inner exporter assumed down, batches are dropped (returns Success)
///   HalfOpen → after cooldown expires, one probe batch goes through
///
/// This protects the application when Jaeger/OTEL Collector is down:
///   - Prevents 30-second export timeouts from blocking the batch processor queue
///   - Reduces log noise from continuous export failures
///   - Automatically recovers when the collector comes back
/// </summary>
public class CircuitBreakerTraceExporter : BaseExporter<Activity>
{
    private readonly BaseExporter<Activity> _inner;
    private readonly ILogger<CircuitBreakerTraceExporter> _logger;

    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;

    private int _failureCount;
    private DateTime _openUntil = DateTime.MinValue;
    private int _halfOpenProbeActive; // 0 = no probe, 1 = probe in progress
    private readonly object _lock = new();

    public CircuitBreakerTraceExporter(
        BaseExporter<Activity> inner,
        ILogger<CircuitBreakerTraceExporter> logger,
        int failureThreshold = 5,
        TimeSpan? openDuration = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            if (_failureCount >= _failureThreshold)
            {
                _logger.LogInformation(
                    "Trace export circuit breaker CLOSED. Export recovered after cooldown");
            }
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
            if (_failureCount >= _failureThreshold && _failureCount == _failureThreshold)
            {
                _openUntil = DateTime.UtcNow.Add(_openDuration);
                _logger.LogWarning(
                    "Trace export circuit breaker OPEN. Dropping traces for {Duration}s after {Failures} consecutive failures",
                    _openDuration.TotalSeconds, _failureCount);
            }
            else if (_failureCount >= _failureThreshold)
            {
                // Extend cooldown on repeated failures in half-open state
                _openUntil = DateTime.UtcNow.Add(_openDuration);
            }
        }
    }

    public override ExportResult Export(in Batch<Activity> batch)
    {
        if (IsOpen)
        {
            // Circuit is open — drop traces silently to prevent cascading timeouts.
            // Return Success so the batch processor doesn't log export failures.
            return ExportResult.Success;
        }

        try
        {
            var result = _inner.Export(batch);

            if (result == ExportResult.Success)
            {
                RecordSuccess();
            }
            else
            {
                RecordFailure();
            }

            // Always return Success to prevent the batch processor from retrying
            return ExportResult.Success;
        }
        catch (Exception ex)
        {
            RecordFailure();
            _logger.LogDebug(ex, "Trace export failed ({FailureCount}/{Threshold})",
                _failureCount, _failureThreshold);

            // Don't propagate — the application must not fail because of observability
            return ExportResult.Success;
        }
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        return _inner.Shutdown(timeoutMilliseconds);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
