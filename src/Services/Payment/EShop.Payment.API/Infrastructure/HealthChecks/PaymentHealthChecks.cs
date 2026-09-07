using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.Payment.API.Infrastructure.HealthChecks;

/// <summary>
/// Payment had <c>services.AddHealthChecks()</c> with no checks registered at all, so its
/// <c>/health/ready</c> (<c>Predicate = _ =&gt; true</c>) evaluated an empty set and reported
/// Healthy unconditionally, and its <c>/health/live</c> (<c>Predicate = _ =&gt; false</c>)
/// could not fail by construction. Both endpoints answered 200 whatever state the service was
/// in — which matters now that Stage 4 gives payment-api a Kubernetes liveness probe, because a
/// probe wired to an endpoint that cannot fail is worse than no probe: it looks like coverage.
///
/// These are modelled on the readiness/liveness pair every other service already has.
/// </summary>
public sealed class PaymentReadinessHealthCheck : IHealthCheck
{
    private readonly PaymentDbContext _dbContext;
    private readonly ILogger<PaymentReadinessHealthCheck> _logger;

    public PaymentReadinessHealthCheck(
        PaymentDbContext dbContext,
        ILogger<PaymentReadinessHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await _dbContext.PaymentTransactions.AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy("Payment service is ready");
        }
        catch (Exception ex)
        {
            // Logged here and deliberately not echoed into the response: the health endpoints
            // are anonymous, and a raw Npgsql error carries the host, database and username.
            _logger.LogError(ex, "Payment readiness health check failed");
            return HealthCheckResult.Unhealthy("Payment service is not ready");
        }
    }
}

/// <summary>
/// Liveness: is the process itself running and able to serve. Must not touch an external
/// dependency — a liveness probe that fails on a database blip restarts a pod that would
/// otherwise have recovered on its own.
/// </summary>
public sealed class PaymentLivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(HealthCheckResult.Healthy("Payment service is alive"));
    }
}
